import { Router } from "express";
import { z } from "zod";
import {
  createRuleSchema,
  reorderRulesSchema,
  updateRuleSchema,
  type Rule,
  type RuleConditionInput,
} from "@clam/core";
import { db } from "../db/client.js";
import { Prisma } from "../generated/prisma/index.js";
import {
  applyPlan,
  buildPlan,
  countMatches,
  countWins,
  loadCategoryNames,
  loadPlanTransactions,
  loadRules,
  toPreview,
  toRule,
} from "../lib/rules.js";

export const rulesRouter = Router();

const withConditions = { conditions: { orderBy: { position: "asc" as const } } };

function conditionCreateData(conditions: RuleConditionInput[]) {
  return conditions.map((c, i) => ({
    field: c.field,
    operator: c.operator,
    value: c.value,
    negate: c.negate,
    position: i,
  }));
}

rulesRouter.get("/", async (_req, res) => {
  const rules = await db.rule.findMany({
    include: withConditions,
    orderBy: [{ kind: "asc" }, { position: "asc" }],
  });
  res.json(rules.map(toRule));
});

rulesRouter.post("/", async (req, res) => {
  const parsed = createRuleSchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ errors: z.flattenError(parsed.error).fieldErrors });
    return;
  }
  const { kind, joinOperator, conditions } = parsed.data;

  // New rules go to the bottom of their list: a rule you just wrote must never
  // silently outrank one you already trust.
  const last = await db.rule.findFirst({ where: { kind }, orderBy: { position: "desc" } });

  try {
    const rule = await db.rule.create({
      data: {
        kind,
        joinOperator,
        position: (last?.position ?? -1) + 1,
        bank: parsed.data.bank ?? null,
        categoryId: kind === "Category" ? (parsed.data.categoryId ?? null) : null,
        bucket: kind === "Bucket" ? (parsed.data.bucket ?? null) : null,
        conditions: { create: conditionCreateData(conditions) },
      },
      include: withConditions,
    });
    res.status(201).json(toRule(rule));
  } catch (err) {
    if (err instanceof Prisma.PrismaClientKnownRequestError && err.code === "P2003") {
      res.status(404).json({ error: "Category not found" });
      return;
    }
    throw err;
  }
});

rulesRouter.patch("/:id", async (req, res) => {
  const parsed = updateRuleSchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ errors: z.flattenError(parsed.error).fieldErrors });
    return;
  }
  const { kind, joinOperator, conditions } = parsed.data;
  const id = req.params.id as string;

  const existing = await db.rule.findUnique({ where: { id } });
  if (!existing) {
    res.status(404).json({ error: "Rule not found" });
    return;
  }

  // Conditions are replaced wholesale rather than diffed — the client edits the
  // set as one thing, and a partial diff would need stable client-side ids.
  const rule = await db.$transaction(async (tx) => {
    await tx.ruleCondition.deleteMany({ where: { ruleId: id } });
    return tx.rule.update({
      where: { id },
      data: {
        kind,
        joinOperator,
        bank: parsed.data.bank ?? null,
        categoryId: kind === "Category" ? (parsed.data.categoryId ?? null) : null,
        bucket: kind === "Bucket" ? (parsed.data.bucket ?? null) : null,
        conditions: { create: conditionCreateData(conditions) },
      },
      include: withConditions,
    });
  });

  res.json(toRule(rule));
});

// Precedence is data, not a heuristic — this is the only thing that decides
// which of two matching rules wins.
rulesRouter.post("/reorder", async (req, res) => {
  const parsed = reorderRulesSchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ errors: z.flattenError(parsed.error).fieldErrors });
    return;
  }
  const { kind, ids } = parsed.data;

  const existing = await db.rule.findMany({ where: { kind }, select: { id: true } });
  const existingIds = new Set(existing.map((r) => r.id));
  if (ids.length !== existingIds.size || ids.some((id) => !existingIds.has(id))) {
    res.status(400).json({ error: "Reorder must list every rule of this kind exactly once" });
    return;
  }

  await db.$transaction(
    ids.map((id, i) => db.rule.update({ where: { id }, data: { position: i } })),
  );

  const rules = await db.rule.findMany({
    where: { kind },
    include: withConditions,
    orderBy: { position: "asc" },
  });
  res.json(rules.map(toRule));
});

rulesRouter.delete("/:id", async (req, res) => {
  const id = req.params.id as string;
  const rule = await db.rule.findUnique({ where: { id } });
  if (!rule) {
    res.status(404).json({ error: "Rule not found" });
    return;
  }

  await db.$transaction(async (tx) => {
    await tx.rule.delete({ where: { id } });
    // Close the gap so positions stay dense; sparse positions still resolve
    // correctly but make the reorder payload confusing to debug.
    const rest = await tx.rule.findMany({
      where: { kind: rule.kind },
      orderBy: { position: "asc" },
      select: { id: true },
    });
    for (const [i, r] of rest.entries()) {
      await tx.rule.update({ where: { id: r.id }, data: { position: i } });
    }
  });

  res.status(204).end();
});

// ─── Dry run ─────────────────────────────────────────────────────────────────

const previewSchema = z.union([
  z.object({ scope: z.literal("all") }),
  z.object({ scope: z.literal("rule"), ruleId: z.string().min(1) }),
  // A draft is previewed before it is ever saved, so you can see what a rule
  // catches without first committing it.
  z.object({
    scope: z.literal("draft"),
    rule: createRuleSchema,
    /** Set when editing: the draft slots into this rule's position, replacing it. */
    ruleId: z.string().min(1).optional(),
  }),
]);

rulesRouter.post("/preview", async (req, res) => {
  const parsed = previewSchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ errors: z.flattenError(parsed.error).fieldErrors });
    return;
  }

  const [saved, transactions, categoryNameById] = await Promise.all([
    loadRules(),
    loadPlanTransactions(),
    loadCategoryNames(),
  ]);

  if (parsed.data.scope === "all") {
    const plan = buildPlan(transactions, saved, categoryNameById);
    res.json(toPreview(plan, categoryNameById));
    return;
  }

  let rules = saved;
  let focusId: string;

  if (parsed.data.scope === "rule") {
    focusId = parsed.data.ruleId;
    if (!saved.some((r) => r.id === focusId)) {
      res.status(404).json({ error: "Rule not found" });
      return;
    }
  } else {
    const { rule: draft, ruleId: editingId } = parsed.data;
    const replacing = editingId ? saved.find((r) => r.id === editingId) : undefined;
    focusId = "__draft__";

    const lastPosition = saved
      .filter((r) => r.kind === draft.kind)
      .reduce((max, r) => Math.max(max, r.position), -1);

    const draftRule: Rule = {
      id: focusId,
      kind: draft.kind,
      position: replacing?.position ?? lastPosition + 1,
      joinOperator: draft.joinOperator,
      bank: draft.bank ?? null,
      categoryId: draft.kind === "Category" ? (draft.categoryId ?? null) : null,
      bucket: draft.kind === "Bucket" ? (draft.bucket ?? null) : null,
      createdAt: new Date().toISOString(),
      conditions: draft.conditions.map((c, i) => ({
        id: `draft-${i}`,
        field: c.field,
        operator: c.operator,
        value: c.value,
        negate: c.negate,
        position: i,
      })),
    };

    rules = [...saved.filter((r) => r.id !== replacing?.id), draftRule];
  }

  const focus = rules.find((r) => r.id === focusId);
  if (!focus) {
    res.status(404).json({ error: "Rule not found" });
    return;
  }

  // `matched` counts everything the conditions accept; `won` counts the subset
  // no higher rule claimed first. Without both, a rule reporting zero changes is
  // indistinguishable from a rule that matches nothing at all.
  const plan = buildPlan(transactions, rules, categoryNameById, focusId);
  const matched = countMatches(transactions, focus, categoryNameById);
  const won = countWins(transactions, rules, categoryNameById, focusId);

  res.json(toPreview(plan, categoryNameById, { matched, won }));
});

const applySchema = z.union([
  z.object({ scope: z.literal("all") }),
  z.object({ scope: z.literal("rule"), ruleId: z.string().min(1) }),
]);

rulesRouter.post("/apply", async (req, res) => {
  const parsed = applySchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ errors: z.flattenError(parsed.error).fieldErrors });
    return;
  }

  const [rules, transactions, categoryNameById] = await Promise.all([
    loadRules(),
    loadPlanTransactions(),
    loadCategoryNames(),
  ]);

  const focusId = parsed.data.scope === "rule" ? parsed.data.ruleId : undefined;
  if (focusId && !rules.some((r) => r.id === focusId)) {
    res.status(404).json({ error: "Rule not found" });
    return;
  }

  // Recomputed from live data rather than replaying the previewed plan, so an
  // import that landed between preview and confirm is included rather than
  // silently skipped. The counts come back so the client can report the truth.
  const plan = buildPlan(transactions, rules, categoryNameById, focusId);
  const result = await applyPlan(plan);
  res.json({ ...result, affected: plan.rows.length, pinnedSkipped: plan.pinnedSkipped });
});
