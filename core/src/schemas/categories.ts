import { z } from "zod";

// 6-digit hex colour, e.g. "#14b8a6".
const hexColor = z.string().regex(/^#[0-9a-fA-F]{6}$/, "Must be a hex colour like #14b8a6");

// isFixed / isDirectDebit are optional — Prisma defaults them to false at the DB
// level, so an omitted flag is treated as false on create.
export const createCategorySchema = z.object({
  name: z.string().trim().min(1, "Name is required").max(40, "Name is too long"),
  color: hexColor,
  isFixed: z.boolean().optional(),
  isDirectDebit: z.boolean().optional(),
});

export const updateCategorySchema = z.object({
  name: z.string().trim().min(1).max(40).optional(),
  color: hexColor.optional(),
  isFixed: z.boolean().optional(),
  isDirectDebit: z.boolean().optional(),
});

export type CreateCategoryInput = z.infer<typeof createCategorySchema>;
export type UpdateCategoryInput = z.infer<typeof updateCategorySchema>;

export type Category = {
  id: string;
  name: string;
  color: string;
  isFixed: boolean;
  isDirectDebit: boolean;
};
