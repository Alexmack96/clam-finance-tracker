import type { Bucket } from "@clam/core";

// A transaction as far as the savings score cares: when, direction, whose, how much,
// and which Bucket. `amount` is always positive; sign comes from `type`.
export interface SavingsTxn {
  date: string; // ISO date; the month key is date.slice(0, 7)
  type: "Income" | "Expense";
  owner: string; // "Alex" | "Casey" | "Joint" | ...
  amount: string | number;
  bucket: Bucket | null;
}

export interface MonthAgg {
  spend: number; // owner-weighted net spend across null / Needs / Wants
  wantsSpend: number; // the Wants slice of it (the discretionary footnote)
}

// Personal-view owner weighting: the viewer's own transactions count in full, Joint
// transactions count half (shared equally), everyone else's count nothing.
export function ownerWeight(txnOwner: string, viewer: string): number {
  if (txnOwner === viewer) return 1;
  if (txnOwner === "Joint") return 0.5;
  return 0;
}

/**
 * Net spend per month (YYYY-MM), owner-weighted for `viewer`.
 *
 * The score is spend against a *plan*, not income minus outgoings — the planned
 * figure comes from the salary lookup, which is deliberately independent of
 * buckets. So salary is not income to this function; it is not spending, and a
 * transaction that is neither belongs in Ignore. That is the whole reason
 * Savings and Ignore are skipped in both directions: money set aside, moved back
 * out, or transferred between accounts never touches the score.
 *
 * Income that *does* carry a spending bucket (null / Needs / Wants) is a refund,
 * so it nets off the spend it reverses rather than counting as earnings.
 */
export function aggregateMonthlySpend(
  txns: SavingsTxn[],
  viewer: string,
): Record<string, MonthAgg> {
  const agg: Record<string, MonthAgg> = {};
  for (const t of txns) {
    const b = t.bucket;
    if (b === "Savings" || b === "Ignore") continue;
    const weight = ownerWeight(t.owner, viewer);
    if (weight === 0) continue;
    const key = t.date.slice(0, 7);
    if (!agg[key]) agg[key] = { spend: 0, wantsSpend: 0 };
    const amount = (typeof t.amount === "string" ? parseFloat(t.amount) : t.amount) * weight;
    const signed = t.type === "Income" ? -amount : amount;
    agg[key].spend += signed;
    if (b === "Wants") agg[key].wantsSpend += signed;
  }
  return agg;
}
