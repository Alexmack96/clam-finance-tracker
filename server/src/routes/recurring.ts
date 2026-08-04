import { Router } from "express";
import { z } from "zod";
import {
  detectRecurring,
  monthlyEquivalent,
  setRecurringVerdictSchema,
  type RecurringCandidate,
  type RecurringOwner,
} from "@clam/core";
import { db } from "../db/client.js";
import { Owner, Prisma } from "../generated/prisma/index.js";

export const recurringRouter = Router();

/**
 * Joint is deliberately unsupported for now — a recurring commitment is
 * something a person is on the hook for, and nothing in the data currently
 * carries owner = Joint anyway.
 */
const RECURRING_OWNERS: RecurringOwner[] = ["Alex", "Casey"];

function parseRecurringOwner(value: unknown): RecurringOwner {
  return RECURRING_OWNERS.includes(value as RecurringOwner) ? (value as RecurringOwner) : "Alex";
}

/** `monzo:tx_123` → `monzo`. Null for rows with no external id. */
function bankOf(externalId: string | null): string | null {
  if (!externalId) return null;
  const idx = externalId.indexOf(":");
  return idx > 0 ? externalId.slice(0, idx) : null;
}

recurringRouter.get("/", async (req, res) => {
  const owner = parseRecurringOwner(req.query.owner);

  const [transactions, verdicts] = await Promise.all([
    db.transaction.findMany({
      where: { owner: owner as Owner },
      include: { category: true },
      orderBy: { date: "asc" },
    }),
    db.recurringVerdict.findMany({ where: { owner: owner as Owner } }),
  ]);

  const candidates: RecurringCandidate[] = transactions.map((t) => ({
    description: t.description,
    date: t.date.toISOString(),
    amount: t.amount.toString(),
    type: t.type,
    bank: bankOf(t.externalId),
    bucket: t.bucket,
    categoryName: t.category.name,
  }));

  // Income and expense are detected independently: a merchant that both charges
  // and refunds on a schedule is two different facts about your money.
  const income = detectRecurring(candidates.filter((c) => c.type === "Income"));
  const expense = detectRecurring(candidates.filter((c) => c.type === "Expense"));

  const verdictByDescription = new Map(verdicts.map((v) => [v.description, v]));
  const decorate = (s: ReturnType<typeof detectRecurring>[number]) => {
    const verdict = verdictByDescription.get(s.description);
    return {
      ...s,
      status: verdict?.status ?? null, // null = Proposed
      note: verdict?.note ?? null,
      monthlyEquivalent: monthlyEquivalent(s),
    };
  };

  const decoratedIncome = income.map(decorate);
  const decoratedExpense = expense.map(decorate);

  // Only confirmed, active, non-Ignore series count toward the committed total.
  // Ignore is where card payments and transfers live — including them would
  // report the entire card balance as a monthly commitment.
  const committed = (rows: ReturnType<typeof decorate>[]) =>
    rows
      .filter((s) => s.status === "Confirmed" && s.active && s.bucket !== "Ignore")
      .reduce((sum, s) => sum + s.monthlyEquivalent, 0);

  res.json({
    owner,
    income: decoratedIncome,
    expense: decoratedExpense,
    committedOutPerMonth: committed(decoratedExpense),
    committedInPerMonth: committed(decoratedIncome),
  });
});

/**
 * Set or clear a verdict. A null status deletes the row, returning the series to
 * Proposed — the absence of a decision is the undecided state, so there is no
 * third enum value to keep in sync.
 */
recurringRouter.put("/verdict", async (req, res) => {
  const parsed = setRecurringVerdictSchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ errors: parsed.error.flatten().fieldErrors });
    return;
  }
  const { owner, description, status } = parsed.data;

  if (status === null) {
    await db.recurringVerdict.deleteMany({ where: { owner: owner as Owner, description } });
    res.status(204).end();
    return;
  }

  const verdict = await db.recurringVerdict.upsert({
    where: { owner_description: { owner: owner as Owner, description } },
    create: { owner: owner as Owner, description, status },
    update: { status },
  });
  res.json(verdict);
});

const noteSchema = z.object({
  owner: z.enum(["Alex", "Casey"]),
  description: z.string().min(1).max(400),
  note: z.string().max(500).nullable(),
});

/** Free-text note on a series, for "cancelled 3 Aug, refund pending" style detail. */
recurringRouter.patch("/note", async (req, res) => {
  const parsed = noteSchema.safeParse(req.body);
  if (!parsed.success) {
    res.status(400).json({ errors: parsed.error.flatten().fieldErrors });
    return;
  }
  const { owner, description, note } = parsed.data;

  // A note hangs off a verdict, so there has to be one to hang it on.
  try {
    const updated = await db.recurringVerdict.update({
      where: { owner_description: { owner: owner as Owner, description } },
      data: { note },
    });
    res.json(updated);
  } catch (err) {
    if (err instanceof Prisma.PrismaClientKnownRequestError && err.code === "P2025") {
      res.status(404).json({ error: "Confirm or reject this series before adding a note" });
      return;
    }
    throw err;
  }
});
