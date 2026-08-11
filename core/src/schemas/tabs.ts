import { z } from "zod";

export const tabDirectionSchema = z.enum(["IOwe", "TheyOwe"]);
export const tabStatusSchema = z.enum(["Open", "Settled"]);

/** The widths of the `person` and `description` columns. */
export const MAX_TAB_PERSON_LENGTH = 200;
export const MAX_TAB_DESCRIPTION_LENGTH = 500;

/**
 * A tab is one person owing another for a shared cost, so a million is a ceiling
 * nobody reaches by intent — and it sits well inside the `DECIMAL(18, 2)` column,
 * past which the insert fails rather than the request.
 */
export const MAX_TAB_AMOUNT = 1_000_000;

/** The column keeps two decimal places; a third is rounded away, not stored. */
const withinScale = (amount: number) => Number.isInteger(Math.round(amount * 100));

const tabAmount = z
  .number()
  .positive("Amount must be positive")
  .max(MAX_TAB_AMOUNT, `Amount must be ${MAX_TAB_AMOUNT.toLocaleString("en-GB")} or less`)
  .refine(withinScale, "Amount cannot have more than 2 decimal places");

export const createTabSchema = z.object({
  person: z.string().min(1, "Person is required").max(MAX_TAB_PERSON_LENGTH, "Person is too long"),
  description: z
    .string()
    .min(1, "Description is required")
    .max(MAX_TAB_DESCRIPTION_LENGTH, "Description is too long"),
  amount: tabAmount,
  direction: tabDirectionSchema,
});

const updateTabShape = z.object({
  person: z.string().min(1).max(MAX_TAB_PERSON_LENGTH, "Person is too long").optional(),
  description: z.string().min(1).max(MAX_TAB_DESCRIPTION_LENGTH, "Description is too long").optional(),
  amount: tabAmount.optional(),
  direction: tabDirectionSchema.optional(),
  status: tabStatusSchema.optional(),
  // An ISO instant, not any string: the route hands this straight to the
  // database, where an unparseable one is an error with no field name on it.
  settledAt: z.iso.datetime().optional().nullable(),
});

/**
 * A function of the current instant rather than a constant schema, because one of
 * its rules is about "now" and the clock is an input — a schema that reads
 * `Date.now()` itself cannot be tested at a chosen instant. Callers pass their
 * clock; tests pass a fixed date.
 */
export const updateTabSchemaAt = (now: Date) =>
  updateTabShape
    // Backdating a settlement is the point of the field; forward-dating one
    // records a tab as settled on a day that has not happened.
    .refine((tab) => !tab.settledAt || Date.parse(tab.settledAt) <= now.getTime(), {
      message: "A tab cannot be settled in the future",
      path: ["settledAt"],
    })
    // An explicit `settledAt` wins over the status, so sending both would stamp a
    // settlement date on a tab the same request just reopened.
    .refine((tab) => !(tab.status === "Open" && tab.settledAt), {
      message: "An open tab cannot have a settled date",
      path: ["settledAt"],
    });

export type CreateTabInput = z.infer<typeof createTabSchema>;
export type UpdateTabInput = z.infer<typeof updateTabShape>;

export type Tab = {
  id: string;
  person: string;
  description: string;
  amount: string; // Decimal → string over JSON
  direction: "IOwe" | "TheyOwe";
  status: "Open" | "Settled";
  settledAt: string | null;
  createdAt: string;
  updatedAt: string;
};
