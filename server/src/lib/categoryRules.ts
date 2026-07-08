import type { Category, CategoryRule } from "../generated/prisma/index.js";

export type CategoryRuleWithCategory = CategoryRule & { category: Category };

// Case-insensitive. "*" acts as a wildcard; otherwise the pattern matches
// anywhere in the description (implicit "*pattern*").
export function matchesPattern(description: string, pattern: string): boolean {
  const escaped = pattern.replace(/[.*+?^${}()|[\]\\]/g, (c) => (c === "*" ? ".*" : `\\${c}`));
  return new RegExp(escaped, "i").test(description);
}

// Bank-specific rules win over any-bank (bank: null) rules when both match.
export function resolveRuleCategory(
  rules: CategoryRuleWithCategory[],
  bank: string,
  description: string,
): Category | null {
  const bankSpecific = rules.find((r) => r.bank === bank && matchesPattern(description, r.pattern));
  if (bankSpecific) return bankSpecific.category;

  const anyBank = rules.find((r) => r.bank === null && matchesPattern(description, r.pattern));
  return anyBank ? anyBank.category : null;
}
