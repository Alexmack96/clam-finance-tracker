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
  // The Bucket is the source of truth for savings maths. Once set it can only be
  // flipped between the four — the client never sends null to clear it.
  bucket: z.enum(["Needs", "Wants", "Savings", "Ignore"]).optional(),
  // Explicit unpin — hands the field back to the rules engine.
  categoryPinned: z.boolean().optional(),
  bucketPinned: z.boolean().optional(),
});

transactionsRouter.patch("/:id", async (req, res) => {
  const body = updateSchema.parse(req.body);
  // Editing a field by hand pins it, so "Run all rules" can never stamp over the
  // choice. An explicit pin flag in the same request still wins — that is how a
  // row gets handed back to the rules engine.
  const transaction = await db.transaction.update({
    where: { id: req.params.id },
    data: {
      ...(body.note !== undefined ? { note: body.note } : {}),
      ...(body.categoryId ? { categoryId: body.categoryId, categoryPinned: true } : {}),
      ...(body.owner ? { owner: body.owner } : {}),
      ...(body.reviewed !== undefined ? { reviewed: body.reviewed } : {}),
      ...(body.bucket !== undefined ? { bucket: body.bucket, bucketPinned: true } : {}),
      ...(body.categoryPinned !== undefined ? { categoryPinned: body.categoryPinned } : {}),
      ...(body.bucketPinned !== undefined ? { bucketPinned: body.bucketPinned } : {}),
    },
    include: { category: true },
  });
  res.json(transaction);
});

transactionsRouter.delete("/:id", async (req, res) => {
  await db.transaction.delete({ where: { id: req.params.id } });
  res.status(204).send();
});
