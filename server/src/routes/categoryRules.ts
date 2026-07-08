import { Router } from "express";
import { db } from "../db/client.js";
import { Prisma } from "../generated/prisma/index.js";
import { createCategoryRuleSchema, updateCategoryRuleSchema } from "@clam/core";
import { matchesPattern } from "../lib/categoryRules.js";

export const categoryRulesRouter = Router();

// Recategorizes existing transactions that match a rule's pattern/bank.
// Called on demand (POST /:id/apply) — never automatically, so a rule save
// only affects future imports unless the user explicitly opts in.
async function backfill(pattern: string, bank: string | null, categoryId: string) {
  const candidates = await db.transaction.findMany({
    where: bank ? { externalId: { startsWith: `${bank}:` } } : {},
    select: { id: true, description: true, categoryId: true },
  });
  const toUpdate = candidates
    .filter((t) => t.categoryId !== categoryId && matchesPattern(t.description, pattern))
    .map((t) => t.id);

  if (toUpdate.length > 0) {
    await db.transaction.updateMany({ where: { id: { in: toUpdate } }, data: { categoryId } });
  }
  return toUpdate.length;
}

categoryRulesRouter.get("/", async (_req, res) => {
  const rules = await db.categoryRule.findMany({ orderBy: { createdAt: "asc" } });
  res.json(rules);
});

categoryRulesRouter.post("/", async (req, res) => {
  const parsed = createCategoryRuleSchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ errors: parsed.error.flatten().fieldErrors });
    return;
  }
  const { pattern, categoryId } = parsed.data;
  const bank = parsed.data.bank ?? null;

  try {
    const rule = await db.categoryRule.create({ data: { pattern, bank, categoryId } });
    res.status(201).json(rule);
  } catch (err) {
    if (err instanceof Prisma.PrismaClientKnownRequestError) {
      if (err.code === "P2002") {
        res.status(409).json({ error: `A rule for "${pattern}" (${bank ?? "any bank"}) already exists` });
        return;
      }
      if (err.code === "P2003") {
        res.status(404).json({ error: "Category not found" });
        return;
      }
    }
    throw err;
  }
});

categoryRulesRouter.patch("/:id", async (req, res) => {
  const parsed = updateCategoryRuleSchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ errors: parsed.error.flatten().fieldErrors });
    return;
  }
  try {
    const rule = await db.categoryRule.update({
      where: { id: req.params.id as string },
      data: parsed.data,
    });
    res.json(rule);
  } catch (err) {
    if (err instanceof Prisma.PrismaClientKnownRequestError) {
      if (err.code === "P2002") {
        res.status(409).json({ error: "A rule with this pattern and bank already exists" });
        return;
      }
      if (err.code === "P2025") {
        res.status(404).json({ error: "Rule not found" });
        return;
      }
    }
    throw err;
  }
});

// User-triggered: "run this rule over existing transactions?" (Outlook-style
// rule apply). Never called implicitly by create/update.
categoryRulesRouter.post("/:id/apply", async (req, res) => {
  const rule = await db.categoryRule.findUnique({ where: { id: req.params.id as string } });
  if (!rule) {
    res.status(404).json({ error: "Rule not found" });
    return;
  }
  const recategorized = await backfill(rule.pattern, rule.bank, rule.categoryId);
  res.json({ recategorized });
});

categoryRulesRouter.delete("/:id", async (req, res) => {
  try {
    await db.categoryRule.delete({ where: { id: req.params.id as string } });
  } catch (err) {
    if (err instanceof Prisma.PrismaClientKnownRequestError && err.code === "P2025") {
      res.status(404).json({ error: "Rule not found" });
      return;
    }
    throw err;
  }
  res.status(204).end();
});
