// Pure helpers for the 50/30/20 Bucket maths, kept free of Prisma/Decimal so they can
// be unit-tested in isolation and reused by every surface that sums Buckets.

export type BucketName = "Needs" | "Wants" | "Savings" | "Ignore";

export interface BucketTxn {
  owner: string; // "Alex" | "Casey" | "Joint" | ...
  type: "Income" | "Expense";
  amount: number; // always positive; sign is derived from `type`
  bucket: BucketName | null;
}

// Personal-view owner weighting: the viewer's own transactions count in full, Joint
// transactions count half (shared equally), everyone else's count nothing.
export function ownerWeight(txnOwner: string, viewer: string): number {
  if (txnOwner === viewer) return 1;
  if (txnOwner === "Joint") return 0.5;
  return 0;
}

// Signed net for a single Bucket: expenses add, income (refunds) subtract, each
// owner-weighted. A £50 Wants refund therefore cancels £50 of Wants spend (£25 if
// Joint). Callers pre-filter by month; Savings/Ignore are simply never passed as the
// target bucket for spend gauges.
export function netBucketSpent(txns: BucketTxn[], bucket: BucketName, viewer: string): number {
  return txns.reduce((sum, t) => {
    if (t.bucket !== bucket) return sum;
    const weight = ownerWeight(t.owner, viewer);
    if (weight === 0) return sum;
    const signed = t.type === "Expense" ? t.amount : -t.amount;
    return sum + signed * weight;
  }, 0);
}
