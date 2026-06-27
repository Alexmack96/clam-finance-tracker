import { z } from "zod";

export const tabDirectionSchema = z.enum(["IOwe", "TheyOwe"]);
export const tabStatusSchema = z.enum(["Open", "Settled"]);

export const createTabSchema = z.object({
  person: z.string().min(1, "Person is required"),
  description: z.string().min(1, "Description is required"),
  amount: z.number().positive("Amount must be positive"),
  direction: tabDirectionSchema,
});

export const updateTabSchema = z.object({
  person: z.string().min(1).optional(),
  description: z.string().min(1).optional(),
  amount: z.number().positive().optional(),
  direction: tabDirectionSchema.optional(),
  status: tabStatusSchema.optional(),
  settledAt: z.string().optional().nullable(),
});

export type CreateTabInput = z.infer<typeof createTabSchema>;
export type UpdateTabInput = z.infer<typeof updateTabSchema>;

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
