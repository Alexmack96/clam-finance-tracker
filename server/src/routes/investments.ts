import { Router } from "express";
import { z } from "zod";
import { ID_MAX_LENGTH } from "@clam/core";
import { db } from "../db/client.js";
import { Owner } from "../generated/prisma/index.js";

export const investmentsRouter = Router();

// ─── Helpers ──────────────────────────────────────────────────────────────────

/** Resolve the owner query param to a valid Owner, defaulting to Alex. */
export function parseOwner(value: unknown): Owner {
  return Object.values(Owner).includes(value as Owner) ? (value as Owner) : Owner.Alex;
}

/** Returns "YYYY-MM" for a Date */
function ym(d: Date) {
  return d.toISOString().slice(0, 7);
}

/** Returns "YYYY" for a Date */
function year(d: Date) {
  return d.toISOString().slice(0, 4);
}

/**
 * Computes NAV = sum of all non-pension account values at a given snapshot index.
 * Pension is intentionally excluded (illiquid; tracked separately).
 */
function computeNAV(
  accounts: { category: string; snapshots: { date: Date; value: number }[] }[],
  date: Date,
): number {
  return accounts
    .filter((a) => a.category !== "pension")
    .reduce((sum, a) => {
      const snap = a.snapshots.find((s) => s.date.getTime() === date.getTime());
      return sum + (snap?.value ?? 0);
    }, 0);
}

// ─── GET /api/investments ─────────────────────────────────────────────────────

investmentsRouter.get("/", async (req, res) => {
  const owner = parseOwner(req.query.owner);
  const accounts = await db.investmentAccount.findMany({
    where: { owner },
    orderBy: { sortOrder: "asc" },
    include: { snapshots: { orderBy: { date: "asc" } } },
  });

  // All unique snapshot dates across all accounts, sorted ascending
  const dateSet = new Set<number>();
  for (const acc of accounts) {
    for (const snap of acc.snapshots) {
      dateSet.add(snap.date.getTime());
    }
  }
  const sortedDates = Array.from(dateSet)
    .sort((a, b) => a - b)
    .map((ms) => new Date(ms));

  const now = new Date();
  const currentMonth = ym(now);
  const prevMonthStr = ym(new Date(now.getFullYear(), now.getMonth() - 1, 1));
  const prevYearStr = String(now.getFullYear() - 1);

  // Latest snapshot date per non-pension account (for NAV stats)
  const latestDate = sortedDates.at(-1);
  const prevDate = sortedDates.at(-2);

  // MTD base: last snapshot in the previous calendar month (e.g. Mar 31 when we're in Apr)
  const mtdBaseDate =
    sortedDates.filter((d) => ym(d) === prevMonthStr).at(-1) ??
    sortedDates.filter((d) => ym(d) < currentMonth).at(-1);

  // YTD base: last snapshot in Dec of previous year (i.e. Dec 31 2025); falls back to
  // last snapshot of that year if no December entry exists
  const ytdBaseDate =
    sortedDates.filter((d) => ym(d) === `${prevYearStr}-12`).at(-1) ??
    sortedDates.filter((d) => year(d) === prevYearStr).at(-1);

  // ITD: very first snapshot
  const itdBaseDate = sortedDates.at(0);

  const navLatest = latestDate ? computeNAV(accounts, latestDate) : 0;
  const navPrev = prevDate ? computeNAV(accounts, prevDate) : 0;
  const navMtdBase = mtdBaseDate ? computeNAV(accounts, mtdBaseDate) : 0;
  const navYtdBase = ytdBaseDate ? computeNAV(accounts, ytdBaseDate) : 0;
  const navItdBase = itdBaseDate ? computeNAV(accounts, itdBaseDate) : 0;

  // Latest known pension value (last two non-null snapshots)
  const pensionAccount = accounts.find((a) => a.category === "pension");
  const pensionSnaps = pensionAccount?.snapshots ?? [];
  const latestPension = pensionSnaps.at(-1)?.value ?? null;
  const prevPension = pensionSnaps.at(-2)?.value ?? null;

  const stats = {
    navLatest,
    navPrev,
    dtdPnL: navLatest - navPrev,
    mtdPnL: navLatest - navMtdBase,
    ytdPnL: navLatest - navYtdBase,
    itdPnL: navLatest - navItdBase,
    pension: latestPension,
    pensionPrev: prevPension,
    totalWealth: latestPension !== null ? navLatest + latestPension : null,
  };

  res.json({
    accounts: accounts.map((a) => ({
      id: a.id,
      name: a.name,
      category: a.category,
      rate: a.rate,
      sortOrder: a.sortOrder,
      snapshots: a.snapshots.map((s) => ({
        id: s.id,
        date: s.date.toISOString(),
        value: s.value,
      })),
    })),
    dates: sortedDates.map((d) => d.toISOString()),
    stats,
  });
});

// ─── Shared bounds ───────────────────────────────────────────────────────────
//
// These mirror the .NET API's validators one for one. Both services write the
// same columns, so a value one accepts and the other refuses is a bug in
// whichever is looser.

/** The width of `InvestmentAccount.name`. */
const MAX_ACCOUNT_NAME_LENGTH = 200;

/**
 * `rate` is a percentage, shown as typed. Without a bound a fat-fingered `450` —
 * or a `1e308` from a client bug — is stored and then projected forward. Negative
 * is allowed: a `debt` account's rate is a cost, and negative deposit rates have
 * happened.
 */
const accountRate = z
  .number()
  .min(-100, "Rate must be between -100 and 100 percent")
  .max(100, "Rate must be between -100 and 100 percent");

/** Sterling, and an order of magnitude past anything this app is for. */
const MAX_SNAPSHOT_VALUE = 100_000_000;

/**
 * A snapshot is a reading taken on a day. The floor catches a two-digit year or a
 * unix epoch that survived a client-side date parse, not history.
 */
const EARLIEST_SNAPSHOT = Date.parse("2000-01-01T00:00:00.000Z");

// ─── POST /api/investments/accounts ──────────────────────────────────────────

const createAccountSchema = z.object({
  name: z.string().min(1).max(MAX_ACCOUNT_NAME_LENGTH, "Name is too long"),
  category: z.enum(["pension", "crypto", "equity", "cash", "commodity", "debt"]),
  owner: z.enum(["Alex", "Casey", "Joint"]).optional(),
  rate: accountRate.nullable().optional(),
  sortOrder: z.number().int().optional(),
});

investmentsRouter.post("/accounts", async (req, res) => {
  const body = createAccountSchema.parse(req.body);
  const owner = (body.owner ?? Owner.Alex) as Owner;
  const maxOrder = await db.investmentAccount.aggregate({
    where: { owner },
    _max: { sortOrder: true },
  });
  const account = await db.investmentAccount.create({
    data: {
      name: body.name,
      category: body.category,
      owner,
      rate: body.rate ?? null,
      sortOrder: body.sortOrder ?? (maxOrder._max.sortOrder ?? 0) + 1,
    },
    include: { snapshots: true },
  });
  res.status(201).json(account);
});

// ─── PATCH /api/investments/accounts/:id ─────────────────────────────────────

const updateAccountSchema = z.object({
  name: z.string().min(1).max(MAX_ACCOUNT_NAME_LENGTH, "Name is too long").optional(),
  category: z.enum(["pension", "crypto", "equity", "cash", "commodity", "debt"]).optional(),
  rate: accountRate.nullable().optional(),
  sortOrder: z.number().int().optional(),
});

investmentsRouter.patch("/accounts/:id", async (req, res) => {
  const body = updateAccountSchema.parse(req.body);
  const account = await db.investmentAccount.update({
    where: { id: req.params.id },
    data: body,
  });
  res.json(account);
});

// ─── DELETE /api/investments/accounts/:id ────────────────────────────────────

investmentsRouter.delete("/accounts/:id", async (req, res) => {
  await db.investmentAccount.delete({ where: { id: req.params.id } });
  res.status(204).end();
});

// ─── PUT /api/investments/snapshots (upsert) ─────────────────────────────────

/**
 * A function of the current instant rather than a constant schema, because one of
 * its rules is about "now" and the clock is an input — a schema that reads the
 * clock itself cannot be tested at a chosen instant.
 *
 * A future snapshot is not merely odd: it becomes the newest row, so it wins every
 * "latest value" read and is reported as the current holding.
 */
const upsertSnapshotSchemaAt = (now: Date) =>
  z.object({
    accountId: z.string().min(1).max(ID_MAX_LENGTH, "accountId is not an id"),
    date: z.iso
      .datetime()
      .refine((d) => Date.parse(d) <= now.getTime(), "A snapshot cannot be dated in the future")
      .refine((d) => Date.parse(d) >= EARLIEST_SNAPSHOT, "A snapshot cannot be dated before 2000-01-01"),
    // Negative is allowed on purpose: a `debt` account is a negative holding, and
    // netting it off is the whole reason that category exists.
    value: z.number().min(-MAX_SNAPSHOT_VALUE).max(MAX_SNAPSHOT_VALUE),
  });

investmentsRouter.put("/snapshots", async (req, res) => {
  const now = new Date();
  const body = upsertSnapshotSchemaAt(now).parse(req.body);
  const date = new Date(body.date);
  const snapshot = await db.investmentSnapshot.upsert({
    where: { accountId_date: { accountId: body.accountId, date } },
    update: { value: body.value, updatedAt: now },
    create: { accountId: body.accountId, date, value: body.value },
  });
  res.json({ ...snapshot, date: snapshot.date.toISOString() });
});

// ─── DELETE /api/investments/snapshots/date/:date — all snapshots on a date ──

investmentsRouter.delete("/snapshots/date/:date", async (req, res) => {
  const owner = parseOwner(req.query.owner);
  await db.investmentSnapshot.deleteMany({
    where: { date: new Date(req.params.date), account: { owner } },
  });
  res.status(204).end();
});

// ─── DELETE /api/investments/snapshots/:id ───────────────────────────────────

investmentsRouter.delete("/snapshots/:id", async (req, res) => {
  await db.investmentSnapshot.delete({ where: { id: req.params.id } });
  res.status(204).end();
});
