import { z } from "zod";

// 6-digit hex colour, e.g. "#14b8a6".
const hexColor = z.string().regex(/^#[0-9a-fA-F]{6}$/, "Must be a hex colour like #14b8a6");

export const savingTypes = ["Fixed", "Fun", "Saving"] as const;
export type SavingType = (typeof savingTypes)[number];
export const savingTypeSchema = z.enum(savingTypes);

// savingType is optional on write — Prisma defaults a category to "Fun" at the DB level.
export const createCategorySchema = z.object({
  name: z.string().trim().min(1, "Name is required").max(40, "Name is too long"),
  color: hexColor,
  savingType: savingTypeSchema.optional(),
});

export const updateCategorySchema = z.object({
  name: z.string().trim().min(1).max(40).optional(),
  color: hexColor.optional(),
  savingType: savingTypeSchema.optional(),
});

export type CreateCategoryInput = z.infer<typeof createCategorySchema>;
export type UpdateCategoryInput = z.infer<typeof updateCategorySchema>;

export type Category = {
  id: string;
  name: string;
  color: string;
  savingType: SavingType;
};
