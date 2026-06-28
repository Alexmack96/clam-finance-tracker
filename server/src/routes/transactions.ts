import { Router } from "express";
import { z } from "zod";
import { db } from "../db/client.js";
import { Owner, TransactionType } from "../generated/prisma/index.js";

export const transactionsRouter = Router();

transactionsRouter.get("/", async (req, res) => {
  const { type, categoryId, owner } = req.query;
  const transactions = await db.transaction.findMany({
    where: {
      ...(type ? { type: type as TransactionType } : {}),
      ...(categoryId ? { categoryId: categoryId as string } : {}),
      ...(owner ? { owner: owner as Owner } : {}),
    },
    include: { category: true },
    orderBy: { date: "desc" },
  });
  res.json(transactions);
});

const updateSchema = z.object({
  note: z.string().nullable().optional(),
  categoryId: z.string().min(1).optional(),
  owner: z.enum(["Alex", "Casey", "Joint"]).optional(),
  reviewed: z.boolean().optional(),
  excludeFromSavings: z.boolean().optional(),
  // null clears the override (inherit the category's savingType).
  savingType: z.enum(["Fixed", "Fun", "Saving"]).nullable().optional(),
});

transactionsRouter.patch("/:id", async (req, res) => {
  const body = updateSchema.parse(req.body);
  const transaction = await db.transaction.update({
    where: { id: req.params.id },
    data: {
      ...(body.note !== undefined ? { note: body.note } : {}),
      ...(body.categoryId ? { categoryId: body.categoryId } : {}),
      ...(body.owner ? { owner: body.owner } : {}),
      ...(body.reviewed !== undefined ? { reviewed: body.reviewed } : {}),
      ...(body.excludeFromSavings !== undefined
        ? { excludeFromSavings: body.excludeFromSavings }
        : {}),
      ...(body.savingType !== undefined ? { savingType: body.savingType } : {}),
    },
    include: { category: true },
  });
  res.json(transaction);
});

transactionsRouter.delete("/:id", async (req, res) => {
  await db.transaction.delete({ where: { id: req.params.id } });
  res.status(204).send();
});
