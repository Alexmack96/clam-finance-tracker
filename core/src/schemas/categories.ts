import { z } from "zod";

// 6-digit hex colour, e.g. "#14b8a6".
const hexColor = z.string().regex(/^#[0-9a-fA-F]{6}$/, "Must be a hex colour like #14b8a6");

// Bucket lives in ./rules.ts — a category no longer carries a default mapping.
// Bucketing is decided by Bucket rules, so there is exactly one place to look
// when asking why a transaction landed where it did.

export const createCategorySchema = z.object({
  name: z.string().trim().min(1, "Name is required").max(40, "Name is too long"),
  color: hexColor,
});

export const updateCategorySchema = z.object({
  name: z.string().trim().min(1).max(40).optional(),
  color: hexColor.optional(),
});

export type CreateCategoryInput = z.infer<typeof createCategorySchema>;
export type UpdateCategoryInput = z.infer<typeof updateCategorySchema>;

export type Category = {
  id: string;
  name: string;
  color: string;
};
