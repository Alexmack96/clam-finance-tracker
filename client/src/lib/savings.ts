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
  saved: number; // owner-weighted net saved that month
  variable: number; // owner-weighted net Wants spend (the discretionary footnote)
}

// Personal-view owner weighting: the viewer's own transactions count in full, Joint
// transactions count half (shared equally), everyone else's count nothing.
export function ownerWeight(txnOwner: string, viewer: string): number {
  if (txnOwner === viewer) return 1;
  if (txnOwner === "Joint") return 0.5;
  return 0;
}

// Symmetric Bucket savings model, grouped by YYYY-MM and owner-weighted for `viewer`:
//   saved = nullIncome − nullSpend − needsNet − wantsNet
// Income adds, expense subtracts, for the buckets that touch the score (null / Needs /
// Wants). Savings and Ignore are excluded in *both* directions — money kept as savings,
// or moved back out, is a wash. `variable` tracks net Wants spend for the footnote.
export function aggregateMonthlySavings(
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
    if (!agg[key]) agg[key] = { saved: 0, variable: 0 };
    const amount = (typeof t.amount === "string" ? parseFloat(t.amount) : t.amount) * weight;
    if (t.type === "Income") {
      // Real income, or a refund that nets its bucket back.
      agg[key].saved += amount;
      if (b === "Wants") agg[key].variable -= amount;
    } else {
      agg[key].saved -= amount;
      if (b === "Wants") agg[key].variable += amount;
    }
  }
  return agg;
}
