import { z } from "zod";

// 6-digit hex colour, e.g. "#14b8a6".
const hexColor = z.string().regex(/^#[0-9a-fA-F]{6}$/, "Must be a hex colour like #14b8a6");

// 50/30/20 Bucket. null = uncategorised. Once set on a category or transaction it can
// only be flipped between these four — never cleared back to null.
export const buckets = ["Needs", "Wants", "Savings", "Ignore"] as const;
export type Bucket = (typeof buckets)[number];
export const bucketSchema = z.enum(buckets);

export const createCategorySchema = z.object({
  name: z.string().trim().min(1, "Name is required").max(40, "Name is too long"),
  color: hexColor,
  bucket: bucketSchema.optional(),
});

export const updateCategorySchema = z.object({
  name: z.string().trim().min(1).max(40).optional(),
  color: hexColor.optional(),
  bucket: bucketSchema.optional(),
});

export type CreateCategoryInput = z.infer<typeof createCategorySchema>;
export type UpdateCategoryInput = z.infer<typeof updateCategorySchema>;

export type Category = {
  id: string;
  name: string;
  color: string;
  bucket: Bucket | null;
};
