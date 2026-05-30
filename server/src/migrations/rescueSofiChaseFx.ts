// One-shot data migration for SoFi/Chase USD→GBP conversion.
// Safe to run on every boot — every step short-circuits when there's nothing
// to do. Logs to stdout (and Sentry via convertWithFallback) when it acts.
//
// Three things it fixes:
//   1. SoFi/Chase staging rows marked "processed" with no matching Transaction
//      (orphans created when the Transaction table was restored from an older
//      snapshot). These get reset to "pending" so step 2 re-inserts them.
//   2. Pending SoFi/Chase staging rows → inserts into Transaction with FX
//      conversion (mirrors the per-bank blocks in /api/admin/process).
//   3. Existing SoFi/Chase Transactions with originalAmount = NULL (imported
//      before the FX bug was discovered) → converts in place.

import { db } from "../db/client.js";
import { convertWithFallback } from "../lib/fxRates.js";

type StagedStatus = "pending" | "processed" | "skipped" | "errored";
type Bank = "sofi" | "chase";

async function findOrphans(bank: Bank): Promise<number[]> {
  const rows = bank === "sofi"
    ? await db.sofiTransaction.findMany({ where: { status: "processed" }, select: { id: true, transactionId: true } })
    : await db.chaseTransaction.findMany({ where: { status: "processed" }, select: { id: true, transactionId: true } });
  const orphans: number[] = [];
  for (const r of rows) {
    const tx = await db.transaction.findUnique({ where: { externalId: `${bank}:${r.transactionId}` }, select: { id: true } });
    if (!tx) orphans.push(r.id);
  }
  return orphans;
}

async function uncategorisedId(): Promise<string> {
  const c = await db.category.upsert({
    where: { name: "Uncategorised" },
    create: { name: "Uncategorised", color: "#d1d5db" },
    update: {},
  });
  return c.id;
}

export async function rescueSofiChaseFx(): Promise<void> {
  // 1. Reset orphans → pending
  const sofiOrphans = await findOrphans("sofi");
  const chaseOrphans = await findOrphans("chase");
  if (sofiOrphans.length > 0) {
    await db.sofiTransaction.updateMany({ where: { id: { in: sofiOrphans } }, data: { status: "pending" } });
    console.log(`[rescueSofiChaseFx] revived ${sofiOrphans.length} SoFi orphans → pending`);
  }
  if (chaseOrphans.length > 0) {
    await db.chaseTransaction.updateMany({ where: { id: { in: chaseOrphans } }, data: { status: "pending" } });
    console.log(`[rescueSofiChaseFx] revived ${chaseOrphans.length} Chase orphans → pending`);
  }

  // 2. Process any pending SoFi/Chase rows with FX conversion
  const categoryId = await uncategorisedId();

  const pendingSofi = await db.sofiTransaction.findMany({ where: { status: "pending" } });
  for (const row of pendingSofi) {
    const usd = parseFloat(row.amount.replace(/,/g, ""));
    let next: StagedStatus;
    if (/^(From|To)\s+(Savings|Checking)/i.test(row.description) || isNaN(usd) || usd === 0) {
      next = "skipped";
    } else {
      const ext = `sofi:${row.transactionId}`;
      const exists = await db.transaction.findUnique({ where: { externalId: ext }, select: { id: true } });
      if (!exists) {
        const txDate = new Date(row.date);
        const { amount: gbp } = await convertWithFallback(usd, "USD", "GBP", txDate, ext);
        await db.transaction.create({
          data: {
            description: row.description, amount: gbp, originalAmount: usd, originalCurrency: "USD",
            type: row.isCredit ? "Income" : "Expense", date: txDate, categoryId, externalId: ext,
            owner: row.owner as "Alex" | "Casey" | "Joint",
          },
        });
      }
      next = "processed";
    }
    await db.sofiTransaction.update({ where: { id: row.id }, data: { status: next } });
  }
  if (pendingSofi.length > 0) console.log(`[rescueSofiChaseFx] processed ${pendingSofi.length} pending SoFi rows`);

  const pendingChase = await db.chaseTransaction.findMany({ where: { status: "pending" } });
  for (const row of pendingChase) {
    const usd = parseFloat(row.amount.replace(/,/g, ""));
    let next: StagedStatus;
    if (isNaN(usd) || usd === 0) {
      next = "skipped";
    } else {
      const ext = `chase:${row.transactionId}`;
      const exists = await db.transaction.findUnique({ where: { externalId: ext }, select: { id: true } });
      if (!exists) {
        const txDate = new Date(row.date);
        const { amount: gbp } = await convertWithFallback(usd, "USD", "GBP", txDate, ext);
        await db.transaction.create({
          data: {
            description: row.description, amount: gbp, originalAmount: usd, originalCurrency: "USD",
            type: row.isCredit ? "Income" : "Expense", date: txDate, categoryId, externalId: ext,
            owner: row.owner as "Alex" | "Casey" | "Joint",
          },
        });
      }
      next = "processed";
    }
    await db.chaseTransaction.update({ where: { id: row.id }, data: { status: next } });
  }
  if (pendingChase.length > 0) console.log(`[rescueSofiChaseFx] processed ${pendingChase.length} pending Chase rows`);

  // 3. Backfill existing FX-less SoFi/Chase Transactions
  const stale = await db.transaction.findMany({
    where: {
      originalAmount: null,
      OR: [{ externalId: { startsWith: "sofi:" } }, { externalId: { startsWith: "chase:" } }],
    },
    select: { id: true, amount: true, date: true, externalId: true },
  });
  for (const r of stale) {
    const usd = Number(r.amount);
    const { amount: gbp } = await convertWithFallback(usd, "USD", "GBP", r.date, r.externalId ?? r.id);
    await db.transaction.update({
      where: { id: r.id },
      data: { amount: gbp, originalAmount: usd, originalCurrency: "USD" },
    });
  }
  if (stale.length > 0) console.log(`[rescueSofiChaseFx] backfilled FX on ${stale.length} existing SoFi/Chase Transactions`);
}
