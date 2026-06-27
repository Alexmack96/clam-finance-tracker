import { Router } from "express";
import { db } from "../db/client.js";
import { TabDirection, TabStatus } from "../generated/prisma/index.js";
import { createTabSchema, updateTabSchema } from "@clam/core";

export const tabsRouter = Router();

// GET /api/tabs?status=all  (default: Open only)
tabsRouter.get("/", async (req, res) => {
  const showAll = req.query.status === "all";
  const tabs = await db.tab.findMany({
    where: showAll ? {} : { status: TabStatus.Open },
    orderBy: { createdAt: "desc" },
  });

  let theyOweMe = 0;
  let iOweThem = 0;
  for (const t of tabs) {
    if (t.status !== TabStatus.Open) continue;
    const amt = parseFloat(t.amount.toString());
    if (t.direction === TabDirection.TheyOwe) theyOweMe += amt;
    else iOweThem += amt;
  }

  res.json({
    tabs,
    totals: {
      theyOweMe: theyOweMe.toFixed(2),
      iOweThem: iOweThem.toFixed(2),
    },
  });
});

// POST /api/tabs
tabsRouter.post("/", async (req, res) => {
  const body = createTabSchema.parse(req.body);
  const tab = await db.tab.create({
    data: {
      person: body.person,
      description: body.description,
      amount: body.amount,
      direction: body.direction as TabDirection,
    },
  });
  res.status(201).json(tab);
});

// PATCH /api/tabs/:id
tabsRouter.patch("/:id", async (req, res) => {
  const body = updateTabSchema.parse(req.body);
  const data: Record<string, unknown> = { ...body };

  if (body.status === "Settled" && body.settledAt === undefined) {
    data.settledAt = new Date();
  }
  if (body.status === "Open") {
    data.settledAt = null;
  }

  const tab = await db.tab.update({ where: { id: req.params.id }, data });
  res.json(tab);
});

// DELETE /api/tabs/:id
tabsRouter.delete("/:id", async (req, res) => {
  await db.tab.delete({ where: { id: req.params.id } });
  res.status(204).end();
});
