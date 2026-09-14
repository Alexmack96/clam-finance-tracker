import { resolveBucket, type Rule } from "@clam/core";

const TYPES = ["Expense", "Income"] as const;

/**
 * Categories no Bucket rule covers on its own, so a transaction moved into one
 * keeps whatever bucket it had.
 *
 * "On its own" means the rule matches on category alone: it is tried against a
 * blank description, so an exception like "Transport + uber → Wants" is not
 * mistaken for Transport's default, and against no bank, so a rule scoped to
 * one bank does not count. Uncategorised is left out; it stays unbucketed on
 * purpose (ADR 0002).
 */
export function categoriesWithoutDefaultBucket<T extends { name: string }>(
  categories: T[],
  rules: Rule[],
): T[] {
  return categories.filter(
    (c) =>
      c.name !== "Uncategorised" &&
      !TYPES.some(
        (type) =>
          resolveBucket({ description: "", type, categoryName: c.name, bank: null }, rules) !==
          null,
      ),
  );
}
