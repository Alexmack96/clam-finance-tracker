import { Router } from "express";
import { z } from "zod";
import { db } from "../db/client.js";
import { Owner, TransactionType } from "../generated/prisma/index.js";

export const transactionsRouter = Router();

const createSchema = z.object({
  description: z.string().min(1),
  amount: z.number().positive(),
  type: z.nativeEnum(TransactionType),
  categoryId: z.string().min(1),
  date: z.string().optional(),
});

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

transactionsRouter.post("/", async (req, res) => {
  const body = createSchema.parse(req.body);
  const transaction = await db.transaction.create({
    data: {
      description: body.description,
      amount: body.amount,
      type: body.type,
      categoryId: body.categoryId,
      ...(body.date ? { date: new Date(body.date) } : {}),
    },
    include: { category: true },
  });
  res.status(201).json(transaction);
});

const updateSchema = z.object({
  note: z.string().nullable().optional(),
  categoryId: z.string().min(1).optional(),
  owner: z.enum(["Alex", "Casey", "Joint"]).optional(),
  reviewed: z.boolean().optional(),
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
    },
    include: { category: true },
  });
  res.json(transaction);
});

transactionsRouter.delete("/:id", async (req, res) => {
  await db.transaction.delete({ where: { id: req.params.id } });
  res.status(204).send();
});
