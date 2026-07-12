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

export const createCategoryRuleSchema = z.object({
  pattern: z.string().trim().min(1, "Pattern is required").max(100, "Pattern is too long"),
  bank: knownBankSchema.nullable().optional(),
  categoryId: z.string().min(1, "Category is required"),
});

export const updateCategoryRuleSchema = z.object({
  pattern: z.string().trim().min(1).max(100).optional(),
  bank: knownBankSchema.nullable().optional(),
  categoryId: z.string().min(1).optional(),
});

export type CreateCategoryRuleInput = z.infer<typeof createCategoryRuleSchema>;
export type UpdateCategoryRuleInput = z.infer<typeof updateCategoryRuleSchema>;

export type CategoryRule = {
  id: string;
  pattern: string;
  bank: KnownBank | null;
  categoryId: string;
  createdAt: string;
};
