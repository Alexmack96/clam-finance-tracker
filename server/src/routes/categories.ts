import { Router } from "express";
import { db } from "../db/client.js";
import { Prisma } from "../generated/prisma/index.js";
import { createCategorySchema, updateCategorySchema } from "@clam/core";

export const categoriesRouter = Router();

categoriesRouter.get("/", async (_req, res) => {
  const categories = await db.category.findMany({
    orderBy: { name: "asc" },
    include: { _count: { select: { transactions: true } } },
  });
  res.json(categories.map(({ _count, ...c }) => ({ ...c, transactionCount: _count.transactions })));
});

categoriesRouter.post("/", async (req, res) => {
  const parsed = createCategorySchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ errors: parsed.error.flatten().fieldErrors });
    return;
  }

  try {
    const category = await db.category.create({ data: parsed.data });
    res.status(201).json(category);
  } catch (err) {
    if (err instanceof Prisma.PrismaClientKnownRequestError && err.code === "P2002") {
      res.status(409).json({ error: `A category named "${parsed.data.name}" already exists` });
      return;
    }
    throw err;
  }
});

categoriesRouter.patch("/:id", async (req, res) => {
  const parsed = updateCategorySchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ errors: parsed.error.flatten().fieldErrors });
    return;
  }

  try {
    const category = await db.category.update({
      where: { id: req.params.id as string },
      data: parsed.data,
    });
    res.json(category);
  } catch (err) {
    if (err instanceof Prisma.PrismaClientKnownRequestError) {
      if (err.code === "P2002") {
        res.status(409).json({ error: `A category named "${parsed.data.name}" already exists` });
        return;
      }
      if (err.code === "P2025") {
        res.status(404).json({ error: "Category not found" });
        return;
      }
    }
    throw err;
  }
});

categoriesRouter.delete("/:id", async (req, res) => {
  const category = await db.category.findUnique({ where: { id: req.params.id as string } });
  if (!category) {
    res.status(404).json({ error: "Category not found" });
    return;
  }

  // Uncategorised is the fallback the import pipeline assigns to; deleting it
  // would break processing, so it can never be removed.
  if (category.name === "Uncategorised") {
    res.status(409).json({ error: "The Uncategorised category cannot be deleted" });
    return;
  }

  // A category in use can't be deleted without orphaning transactions — point the
  // user at merge, which reassigns those transactions before removing the category.
  const inUse = await db.transaction.count({ where: { categoryId: category.id } });
  if (inUse > 0) {
    res.status(409).json({
      error: `"${category.name}" still has ${inUse} transaction${inUse === 1 ? "" : "s"}. Merge it into another category first.`,
    });
    return;
  }

  await db.category.delete({ where: { id: category.id } });
  res.status(204).end();
});

categoriesRouter.post("/:fromId/merge/:toId", async (req, res) => {
  const fromId = req.params.fromId as string;
  const toId = req.params.toId as string;

  if (fromId === toId) {
    res.status(400).json({ error: "Source and target categories must differ" });
    return;
  }

  const [from, to] = await Promise.all([
    db.category.findUnique({ where: { id: fromId } }),
    db.category.findUnique({ where: { id: toId } }),
  ]);

  if (!from) {
    res.status(404).json({ error: "Source category not found" });
    return;
  }
  if (!to) {
    res.status(404).json({ error: "Target category not found" });
    return;
  }

  const { count } = await db.transaction.updateMany({
    where: { categoryId: fromId },
    data: { categoryId: toId },
  });

  await db.category.delete({ where: { id: fromId } });

  res.json({ merged: count, from: from.name, to: to.name });
});
