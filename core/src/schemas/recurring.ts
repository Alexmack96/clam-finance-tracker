import { z } from "zod";

export const RECURRING_CADENCES = [
  "Weekly",
  "Fortnightly",
  "Monthly",
  "Quarterly",
  "Yearly",
] as const;
export type RecurringCadence = (typeof RECURRING_CADENCES)[number];
export const recurringCadenceSchema = z.enum(RECURRING_CADENCES);

export const RECURRING_KINDS = ["Income", "Expense"] as const;
export type RecurringKind = (typeof RECURRING_KINDS)[number];

/**
 * A verdict is the only thing worth storing. Cadence, amount, next-due and
 * active state are recomputed on every request — snapshotting them would create
 * a second source of truth that goes stale the moment a statement lands.
 * No row means "Proposed": the absence of a decision *is* the state.
 */
export const RECURRING_STATUSES = ["Confirmed", "Rejected"] as const;
export type RecurringStatus = (typeof RECURRING_STATUSES)[number];
export const recurringStatusSchema = z.enum(RECURRING_STATUSES);

/** Joint is deliberately absent — recurring items belong to a person for now. */
export const RECURRING_OWNERS = ["Alex", "Casey"] as const;
export type RecurringOwner = (typeof RECURRING_OWNERS)[number];
export const recurringOwnerSchema = z.enum(RECURRING_OWNERS);

export const setRecurringVerdictSchema = z.object({
  owner: recurringOwnerSchema,
  description: z.string().min(1).max(400),
  /** null clears the verdict, returning the series to Proposed. */
  status: recurringStatusSchema.nullable(),
});
export type SetRecurringVerdictInput = z.infer<typeof setRecurringVerdictSchema>;

/** What a Bucket implies about the nature of a recurring item. */
export const RECURRING_TYPE_BY_BUCKET = {
  Needs: "Bill",
  Wants: "Subscription",
  Savings: "Investment",
  Ignore: "Transfer",
} as const;

export function recurringTypeLabel(bucket: keyof typeof RECURRING_TYPE_BY_BUCKET | null): string {
  return bucket ? RECURRING_TYPE_BY_BUCKET[bucket] : "Unclassified";
}
