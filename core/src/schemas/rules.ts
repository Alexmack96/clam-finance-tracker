import { z } from "zod";

export const KNOWN_BANKS = [
  "monzo",
  "flex",
  "amex",
  "barclays",
  "santander",
  "hsbc",
  "sofi",
  "chase",
] as const;
export type KnownBank = (typeof KNOWN_BANKS)[number];
export const knownBankSchema = z.enum(KNOWN_BANKS);

// 50/30/20 Bucket. null = uncategorised.
export const BUCKETS = ["Needs", "Wants", "Savings", "Ignore"] as const;
export type Bucket = (typeof BUCKETS)[number];
export const bucketSchema = z.enum(BUCKETS);
/** @deprecated Prefer `BUCKETS`; kept so existing call sites keep compiling. */
export const buckets = BUCKETS;

export const RULE_KINDS = ["Category", "Bucket"] as const;
export type RuleKind = (typeof RULE_KINDS)[number];
export const ruleKindSchema = z.enum(RULE_KINDS);

export const RULE_JOINS = ["AND", "OR"] as const;
export type RuleJoin = (typeof RULE_JOINS)[number];
export const ruleJoinSchema = z.enum(RULE_JOINS);

/**
 * Fields a condition can test. `Category` is only meaningful on a Bucket rule —
 * Category rules run first in the pipeline, so at that point no category has
 * been decided yet.
 */
export const RULE_FIELDS = ["Description", "Category", "Type"] as const;
export type RuleField = (typeof RULE_FIELDS)[number];
export const ruleFieldSchema = z.enum(RULE_FIELDS);

export const RULE_OPERATORS = ["Contains", "StartsWith", "EndsWith", "Exact"] as const;
export type RuleOperator = (typeof RULE_OPERATORS)[number];
export const ruleOperatorSchema = z.enum(RULE_OPERATORS);

export const RULE_OPERATOR_LABELS: Record<RuleOperator, string> = {
  Contains: "contains",
  StartsWith: "starts with",
  EndsWith: "ends with",
  Exact: "equals",
};

export const RULE_FIELD_LABELS: Record<RuleField, string> = {
  Description: "Description",
  Category: "Category",
  Type: "Type",
};

// ─── Input schemas ───────────────────────────────────────────────────────────

export const ruleConditionInputSchema = z.object({
  field: ruleFieldSchema.default("Description"),
  operator: ruleOperatorSchema.default("Contains"),
  value: z.string().trim().min(1, "Value is required").max(200, "Value is too long"),
  negate: z.boolean().default(false),
});

const ruleBase = z.object({
  kind: ruleKindSchema,
  joinOperator: ruleJoinSchema.default("AND"),
  bank: knownBankSchema.nullable().optional(),
  categoryId: z.string().min(1).nullable().optional(),
  bucket: bucketSchema.nullable().optional(),
  conditions: z.array(ruleConditionInputSchema).min(1, "At least one condition is required"),
});

/**
 * A rule with only negated conditions would match nearly every transaction, so
 * it is rejected rather than left as a foot-gun that fires unattended during an
 * import. The output must also match the kind — a Category rule with no
 * category assigns nothing.
 */
export const createRuleSchema = ruleBase
  .refine((r) => r.conditions.some((c) => !c.negate), {
    message: "A rule needs at least one non-negated condition",
    path: ["conditions"],
  })
  .refine((r) => r.kind !== "Category" || !!r.categoryId, {
    message: "A category rule must set a category",
    path: ["categoryId"],
  })
  .refine((r) => r.kind !== "Bucket" || !!r.bucket, {
    message: "A bucket rule must set a bucket",
    path: ["bucket"],
  });

export const updateRuleSchema = createRuleSchema;

export const reorderRulesSchema = z.object({
  kind: ruleKindSchema,
  ids: z.array(z.string().min(1)).min(1),
});

export type RuleConditionInput = z.infer<typeof ruleConditionInputSchema>;
export type CreateRuleInput = z.infer<typeof createRuleSchema>;
export type UpdateRuleInput = z.infer<typeof updateRuleSchema>;
export type ReorderRulesInput = z.infer<typeof reorderRulesSchema>;

// ─── Wire types ──────────────────────────────────────────────────────────────

export type RuleCondition = {
  id: string;
  field: RuleField;
  operator: RuleOperator;
  value: string;
  negate: boolean;
  position: number;
};

export type Rule = {
  id: string;
  kind: RuleKind;
  position: number;
  joinOperator: RuleJoin;
  bank: KnownBank | null;
  categoryId: string | null;
  bucket: Bucket | null;
  conditions: RuleCondition[];
  createdAt: string;
};

/** One transaction the run would change, as returned by a dry-run. */
export type RulePreviewRow = {
  id: string;
  date: string;
  description: string;
  amount: number;
  type: "Income" | "Expense";
  bank: string | null;
  currentCategory: string;
  proposedCategory: string | null;
  currentBucket: Bucket | null;
  proposedBucket: Bucket | null;
  /** Which rule won, per field — null when nothing claimed it. */
  categoryRuleId: string | null;
  bucketRuleId: string | null;
};

export type RulePreview = {
  /** Rows the run would actually change. */
  rows: RulePreviewRow[];
  scanned: number;
  categoryChanges: number;
  bucketChanges: number;
  /**
   * Single-rule previews only. `matched` counts every transaction the rule's
   * conditions accept; `won` counts the subset no higher-positioned rule
   * claimed first. A rule can match plenty and win nothing.
   */
  matched?: number;
  won?: number;
  /** Rows skipped because the field was pinned by hand. */
  pinnedSkipped: number;
};
