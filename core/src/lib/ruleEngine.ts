import type { Bucket, Rule, RuleCondition, RuleField, RuleOperator } from "../schemas/rules.js";

/**
 * The single source of truth for what a rule matches.
 *
 * Lives in `core` rather than `server` so the dry-run, the import pipeline and
 * the client all answer "does this rule match?" identically. ag-grid's filter
 * engine is *not* used for this — it only exists in the browser, and rules have
 * to run server-side during an unattended `/process`.
 */

/** The minimum a transaction needs to expose to be matched. */
export type MatchableTransaction = {
  description: string;
  type: "Income" | "Expense";
  /** Category name, or null while Category rules are still being resolved. */
  categoryName: string | null;
  /** Namespace from `externalId` (`monzo`, `amex`, …), or null. */
  bank: string | null;
};

/** Matching is always case-insensitive — bank descriptions are inconsistently cased. */
function compare(haystack: string, operator: RuleOperator, needle: string): boolean {
  const h = haystack.toLowerCase();
  const n = needle.trim().toLowerCase();
  switch (operator) {
    case "Contains":
      return h.includes(n);
    case "StartsWith":
      return h.startsWith(n);
    case "EndsWith":
      return h.endsWith(n);
    case "Exact":
      return h === n;
  }
}

function fieldValue(tx: MatchableTransaction, field: RuleField): string | null {
  switch (field) {
    case "Description":
      return tx.description;
    case "Category":
      return tx.categoryName;
    case "Type":
      return tx.type;
  }
}

export function matchesCondition(tx: MatchableTransaction, condition: RuleCondition): boolean {
  const value = fieldValue(tx, condition.field);
  // An unresolved field can't satisfy a positive condition, and can't violate a
  // negative one — "not contains X" is vacuously true when there's nothing to test.
  if (value === null) return condition.negate;
  const hit = compare(value, condition.operator, condition.value);
  return condition.negate ? !hit : hit;
}

/**
 * `joinOperator` applies to the positive conditions only. Negated conditions are
 * always ANDed as exclusions, so a rule reads:
 *
 *     (any | all of the positives)  AND  none of the negatives
 *
 * This diverges from ag-grid, which applies one join operator across the whole
 * set. ag-grid filters a table you are looking at; a rule fires unattended weeks
 * later, and "include this, except that" is what a rule with exceptions means.
 * It also makes `(A OR B) AND NOT C` expressible without a nesting UI.
 */
export function matchesRule(tx: MatchableTransaction, rule: Rule): boolean {
  if (rule.bank !== null && rule.bank !== tx.bank) return false;

  const positives = rule.conditions.filter((c) => !c.negate);
  const negatives = rule.conditions.filter((c) => c.negate);

  if (negatives.some((c) => !matchesCondition(tx, c))) return false;
  if (positives.length === 0) return false;

  return rule.joinOperator === "OR"
    ? positives.some((c) => matchesCondition(tx, c))
    : positives.every((c) => matchesCondition(tx, c));
}

/**
 * First match wins, in `position` order. There is no specificity heuristic:
 * with N conditions and negations, "which rule is more specific" has no answer
 * a user could predict, so precedence is explicit and user-owned instead.
 */
export function resolveRule(tx: MatchableTransaction, rules: Rule[]): Rule | null {
  const ordered = [...rules].sort((a, b) => a.position - b.position);
  return ordered.find((r) => matchesRule(tx, r)) ?? null;
}

export function resolveCategoryId(tx: MatchableTransaction, rules: Rule[]): string | null {
  const winner = resolveRule(
    tx,
    rules.filter((r) => r.kind === "Category"),
  );
  return winner?.categoryId ?? null;
}

export function resolveBucket(tx: MatchableTransaction, rules: Rule[]): Bucket | null {
  const winner = resolveRule(
    tx,
    rules.filter((r) => r.kind === "Bucket"),
  );
  return winner?.bucket ?? null;
}

export type ResolvedAssignment = {
  categoryId: string | null;
  categoryRuleId: string | null;
  bucket: Bucket | null;
  bucketRuleId: string | null;
};

/**
 * The two-pass pipeline. Category rules resolve first, then the resulting
 * category name is fed to the Bucket pass — which is what makes
 * `category = Transport AND description contains "uber" → Wants` work.
 */
export function resolveAssignment(
  tx: MatchableTransaction,
  rules: Rule[],
  categoryNameById: Map<string, string>,
): ResolvedAssignment {
  const categoryWinner = resolveRule(
    tx,
    rules.filter((r) => r.kind === "Category"),
  );
  const categoryId = categoryWinner?.categoryId ?? null;

  const categoryName =
    categoryId !== null ? (categoryNameById.get(categoryId) ?? null) : tx.categoryName;

  const bucketWinner = resolveRule(
    { ...tx, categoryName },
    rules.filter((r) => r.kind === "Bucket"),
  );

  return {
    categoryId,
    categoryRuleId: categoryWinner?.id ?? null,
    bucket: bucketWinner?.bucket ?? null,
    bucketRuleId: bucketWinner?.id ?? null,
  };
}

/** `monzo:tx_123` → `monzo`. Null-safe for rows with no external id. */
export function bankOf(externalId: string | null | undefined): string | null {
  if (!externalId) return null;
  const idx = externalId.indexOf(":");
  return idx === -1 ? null : externalId.slice(0, idx);
}
