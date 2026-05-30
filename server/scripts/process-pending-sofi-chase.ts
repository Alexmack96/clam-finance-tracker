// One-shot: process pending SoFi/Chase staging rows with USD→GBP conversion.
// Matches the per-bank blocks in import.ts /process.
import { config } from "dotenv";
import { resolve } from "path";
import { fileURLToPath } from "url";
const here = fileURLToPath(new URL(".", import.meta.url));
config({ path: resolve(here, "../../.env") });
const { db } = await import("../src/db/client.js");
const { convert } = await import("../src/lib/fxRates.js");

async function findCategory(name: string) {
  return db.category.upsert({
    where: { name },
    create: { name, color: "#d1d5db" },
    update: {},
  });
}

const uncategorised = await findCategory("Uncategorised");

let processed = 0, skipped = 0, errored = 0;

// SoFi
const pendingSofi = await db.sofiTransaction.findMany({ where: { status: "pending" } });
for (const row of pendingSofi) {
  const usdAmount = parseFloat(row.amount.replace(/,/g, ""));
  let next: "pending" | "processed" | "skipped" | "errored";
  if (/^(From|To)\s+(Savings|Checking)/i.test(row.description)) { next = "skipped"; skipped++; }
  else if (isNaN(usdAmount) || usdAmount === 0) { next = "skipped"; skipped++; }
  else {
    const ext = `sofi:${row.transactionId}`;
    const exists = await db.transaction.findUnique({ where: { externalId: ext }, select: { id: true } });
    if (exists) { next = "processed"; processed++; }
    else {
      try {
        const txDate = new Date(row.date);
        const gbp = await convert(usdAmount, "USD", "GBP", txDate);
        await db.transaction.create({
          data: {
            description: row.description, amount: gbp, originalAmount: usdAmount, originalCurrency: "USD",
            type: row.isCredit ? "Income" : "Expense", date: txDate, categoryId: uncategorised.id,
            externalId: ext, owner: row.owner as "Alex" | "Casey" | "Joint",
          },
        });
        next = "processed"; processed++;
      } catch (err) {
        console.error(`sofi:${row.transactionId}`, err);
        next = "errored"; errored++;
      }
    }
  }
  await db.sofiTransaction.update({ where: { id: row.id }, data: { status: next } });
}

// Chase
const pendingChase = await db.chaseTransaction.findMany({ where: { status: "pending" } });
for (const row of pendingChase) {
  const usdAmount = parseFloat(row.amount.replace(/,/g, ""));
  let next: "pending" | "processed" | "skipped" | "errored";
  if (isNaN(usdAmount) || usdAmount === 0) { next = "skipped"; skipped++; }
  else {
    const ext = `chase:${row.transactionId}`;
    const exists = await db.transaction.findUnique({ where: { externalId: ext }, select: { id: true } });
    if (exists) { next = "processed"; processed++; }
    else {
      try {
        const txDate = new Date(row.date);
        const gbp = await convert(usdAmount, "USD", "GBP", txDate);
        await db.transaction.create({
          data: {
            description: row.description, amount: gbp, originalAmount: usdAmount, originalCurrency: "USD",
            type: row.isCredit ? "Income" : "Expense", date: txDate, categoryId: uncategorised.id,
            externalId: ext, owner: row.owner as "Alex" | "Casey" | "Joint",
          },
        });
        next = "processed"; processed++;
      } catch (err) {
        console.error(`chase:${row.transactionId}`, err);
        next = "errored"; errored++;
      }
    }
  }
  await db.chaseTransaction.update({ where: { id: row.id }, data: { status: next } });
}

console.log(`processed=${processed}  skipped=${skipped}  errored=${errored}`);
process.exit(0);
