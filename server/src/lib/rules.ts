import {
  bankOf,
  matchesRule,
  resolveRule,
  type Bucket,
  type KnownBank,
  type MatchableTransaction,
  type Rule,
  type RulePreview,
  type RulePreviewRow,
} from "@clam/core";
import { db } from "../db/client.js";

/**
 * Server-side plumbing around the shared engine in `@clam/core`. The matching
 * itself lives in core so the dry-run, `/process` and the client can never
 * disagree about what a rule does.
 */

type DbRule = {
  id: string;
  kind: string;
  position: number;
  joinOperator: string;
  bank: string | null;
  categoryId: string | null;
  bucket: string | null;
  createdAt: Date;
  conditions: {
    id: string;
    field: string;
    operator: string;
    value: string;
    negate: boolean;
    position: number;
  }[];
};

export function toRule(r: DbRule): Rule {
  return {
    id: r.id,
    kind: r.kind as Rule["kind"],
    position: r.position,
    joinOperator: r.joinOperator as Rule["joinOperator"],
    bank: r.bank as KnownBank | null,
    categoryId: r.categoryId,
    bucket: r.bucket as Bucket | null,
    createdAt: r.createdAt.toISOString(),
    conditions: [...r.conditions]
      .sort((a, b) => a.position - b.position)
      .map((c) => ({
        id: c.id,
        field: c.field as Rule["conditions"][number]["field"],
        operator: c.operator as Rule["conditions"][number]["operator"],
        value: c.value,
        negate: c.negate,
        position: c.position,
      })),
  };
}

export async function loadRules(): Promise<Rule[]> {
  const rows = await db.rule.findMany({
    include: { conditions: true },
    orderBy: [{ kind: "asc" }, { position: "asc" }],
  });
  return rows.map(toRule);
}

type PlanTransaction = {
  id: string;
  date: Date;
  description: string;
  amount: unknown;
  type: "Income" | "Expense";
  externalId: string | null;
  categoryId: string;
  bucket: string | null;
  categoryPinned: boolean;
  bucketPinned: boolean;
};

export type PlanRow = {
  tx: PlanTransaction;
  nextCategoryId: string | null;
  nextBucket: Bucket | null;
  categoryRuleId: string | null;
  bucketRuleId: string | null;
};

export type Plan = {
  rows: PlanRow[];
  scanned: number;
  pinnedSkipped: number;
};

/**
 * Runs the two-pass pipeline over every transaction and returns only the rows
 * whose category or bucket would actually change.
 *
 * `focusRuleId` narrows the result to rows that rule *wins* — not merely
 * matches. A rule lower down the list can match plenty and win nothing, which
 * is exactly the information the single-rule preview has to surface.
 */
export function buildPlan(
  transactions: PlanTransaction[],
  rules: Rule[],
  categoryNameById: Map<string, string>,
  focusRuleId?: string,
): Plan {
  const categoryRules = rules.filter((r) => r.kind === "Category");
  const bucketRules = rules.filter((r) => r.kind === "Bucket");

  const rows: PlanRow[] = [];
  let pinnedSkipped = 0;

  for (const tx of transactions) {
    const base: MatchableTransaction = {
      description: tx.description,
      type: tx.type,
      categoryName: categoryNameById.get(tx.categoryId) ?? null,
      bank: bankOf(tx.externalId),
    };

    const categoryWinner = resolveRule(base, categoryRules);
    const proposedCategoryId = categoryWinner?.categoryId ?? null;

    // A pinned category is never replaced, so the bucket pass must see the
    // category that will actually be in place — not the one a rule wanted.
    const effectiveCategoryId = tx.categoryPinned
      ? tx.categoryId
      : (proposedCategoryId ?? tx.categoryId);

    const bucketWinner = resolveRule(
      { ...base, categoryName: categoryNameById.get(effectiveCategoryId) ?? null },
      bucketRules,
    );
    const proposedBucket = bucketWinner?.bucket ?? null;

    const categoryChanges =
      !tx.categoryPinned && proposedCategoryId !== null && proposedCategoryId !== tx.categoryId;
    const bucketChanges =
      !tx.bucketPinned && proposedBucket !== null && proposedBucket !== tx.bucket;

    if (tx.categoryPinned && proposedCategoryId !== null && proposedCategoryId !== tx.categoryId) {
      pinnedSkipped++;
    } else if (tx.bucketPinned && proposedBucket !== null && proposedBucket !== tx.bucket) {
      pinnedSkipped++;
    }

    if (!categoryChanges && !bucketChanges) continue;

    if (focusRuleId) {
      const wins =
        (categoryChanges && categoryWinner?.id === focusRuleId) ||
        (bucketChanges && bucketWinner?.id === focusRuleId);
      if (!wins) continue;
    }

    rows.push({
      tx,
      nextCategoryId: categoryChanges ? proposedCategoryId : null,
      nextBucket: bucketChanges ? proposedBucket : null,
      categoryRuleId: categoryChanges ? (categoryWinner?.id ?? null) : null,
      bucketRuleId: bucketChanges ? (bucketWinner?.id ?? null) : null,
    });
  }

  return { rows, scanned: transactions.length, pinnedSkipped };
}

/** How many transactions a single rule's conditions accept, ignoring precedence. */
export function countMatches(
  transactions: PlanTransaction[],
  rule: Rule,
  categoryNameById: Map<string, string>,
): number {
  let matched = 0;
  for (const tx of transactions) {
    const m: MatchableTransaction = {
      description: tx.description,
      type: tx.type,
      categoryName: categoryNameById.get(tx.categoryId) ?? null,
      bank: bankOf(tx.externalId),
    };
    if (matchesRule(m, rule)) matched++;
  }
  return matched;
}

/**
 * How many transactions a rule is the *winner* for, whether or not it changes
 * anything. Paired with `countMatches` this is what makes a rule reporting zero
 * changes intelligible: matched 26 / won 3 means higher rules took the rest.
 */
export function countWins(
  transactions: PlanTransaction[],
  rules: Rule[],
  categoryNameById: Map<string, string>,
  focusRuleId: string,
): number {
  const focus = rules.find((r) => r.id === focusRuleId);
  if (!focus) return 0;
  const sameKind = rules.filter((r) => r.kind === focus.kind);
  const categoryRules = rules.filter((r) => r.kind === "Category");

  let wins = 0;
  for (const tx of transactions) {
    const base: MatchableTransaction = {
      description: tx.description,
      type: tx.type,
      categoryName: categoryNameById.get(tx.categoryId) ?? null,
      bank: bankOf(tx.externalId),
    };

    // A Bucket rule is judged against the category that will actually be in
    // place once the Category pass has run — the same context the real run uses.
    let context = base;
    if (focus.kind === "Bucket") {
      const proposed = resolveRule(base, categoryRules)?.categoryId ?? null;
      const effective = tx.categoryPinned ? tx.categoryId : (proposed ?? tx.categoryId);
      context = { ...base, categoryName: categoryNameById.get(effective) ?? null };
    }

    if (resolveRule(context, sameKind)?.id === focusRuleId) wins++;
  }
  return wins;
}

export async function loadPlanTransactions(): Promise<PlanTransaction[]> {
  return (await db.transaction.findMany({
    select: {
      id: true,
      date: true,
      description: true,
      amount: true,
      type: true,
      externalId: true,
      categoryId: true,
      bucket: true,
      categoryPinned: true,
      bucketPinned: true,
    },
    orderBy: { date: "desc" },
  })) as PlanTransaction[];
}

export async function loadCategoryNames(): Promise<Map<string, string>> {
  const categories = await db.category.findMany({ select: { id: true, name: true } });
  return new Map(categories.map((c) => [c.id, c.name]));
}

export function toPreview(
  plan: Plan,
  categoryNameById: Map<string, string>,
  extra?: { matched: number; won: number },
): RulePreview {
  const rows: RulePreviewRow[] = plan.rows.map((r) => ({
    id: r.tx.id,
    date: r.tx.date.toISOString(),
    description: r.tx.description,
    amount: Number(r.tx.amount),
    type: r.tx.type,
    bank: bankOf(r.tx.externalId),
    currentCategory: categoryNameById.get(r.tx.categoryId) ?? "—",
    proposedCategory:
      r.nextCategoryId !== null ? (categoryNameById.get(r.nextCategoryId) ?? "—") : null,
    currentBucket: r.tx.bucket as Bucket | null,
    proposedBucket: r.nextBucket,
    categoryRuleId: r.categoryRuleId,
    bucketRuleId: r.bucketRuleId,
  }));

  return {
    rows,
    scanned: plan.scanned,
    categoryChanges: plan.rows.filter((r) => r.nextCategoryId !== null).length,
    bucketChanges: plan.rows.filter((r) => r.nextBucket !== null).length,
    pinnedSkipped: plan.pinnedSkipped,
    ...(extra ?? {}),
  };
}

/**
 * Writes a plan, grouped by target value so a 1800-row run is a handful of
 * `updateMany`s rather than a row-at-a-time loop. Never touches the pin flags —
 * a pin is only ever set by a hand edit.
 */
export async function applyPlan(plan: Plan): Promise<{
  categoryChanges: number;
  bucketChanges: number;
}> {
  const byCategory = new Map<string, string[]>();
  const byBucket = new Map<string, string[]>();

  for (const row of plan.rows) {
    if (row.nextCategoryId !== null) {
      const list = byCategory.get(row.nextCategoryId) ?? [];
      list.push(row.tx.id);
      byCategory.set(row.nextCategoryId, list);
    }
    if (row.nextBucket !== null) {
      const list = byBucket.get(row.nextBucket) ?? [];
      list.push(row.tx.id);
      byBucket.set(row.nextBucket, list);
    }
  }

  let categoryChanges = 0;
  let bucketChanges = 0;

  await db.$transaction(async (tx) => {
    for (const [categoryId, ids] of byCategory) {
      const { count } = await tx.transaction.updateMany({
        where: { id: { in: ids } },
        data: { categoryId },
      });
      categoryChanges += count;
    }
    for (const [bucket, ids] of byBucket) {
      const { count } = await tx.transaction.updateMany({
        where: { id: { in: ids } },
        data: { bucket: bucket as Bucket },
      });
      bucketChanges += count;
    }
  });

  return { categoryChanges, bucketChanges };
}
