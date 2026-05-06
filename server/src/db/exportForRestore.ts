import { config } from "dotenv";
import { writeFileSync } from "fs";
import { join } from "path";

config({ path: import.meta.dirname + "/../../../.env" });

// Override DATABASE_URL to absolute path so this script works from any CWD.
// client.ts reads DATABASE_URL at load time, so we must set it before importing.
process.env.DATABASE_URL = `file:${join(import.meta.dirname, "../../dev.db")}`;

const { db } = await import("./client.js");

async function main() {
  console.log(`Reading from ${process.env.DATABASE_URL}...`);

  const [
    categories,
    transactions,
    notes,
    tabs,
    investmentAccounts,
    investmentSnapshots,
    monzoApiTransactions,
    amexTransactions,
    barclaysTransactions,
    santanderTransactions,
    hsbcTransactions,
    chaseTransactions,
    sofiTransactions,
    plaidItems,
    plaidTransactions,
  ] = await Promise.all([
    db.category.findMany(),
    db.transaction.findMany(),
    db.note.findMany(),
    db.tab.findMany(),
    db.investmentAccount.findMany(),
    db.investmentSnapshot.findMany(),
    db.monzoApiTransaction.findMany(),
    db.amexTransaction.findMany(),
    db.barclaysTransaction.findMany(),
    db.santanderTransaction.findMany(),
    db.hsbcTransaction.findMany(),
    db.chaseTransaction.findMany(),
    db.sofiTransaction.findMany(),
    db.plaidItem.findMany(),
    db.plaidTransaction.findMany(),
  ]);

  const data = {
    Category: categories,
    Transaction: transactions,
    Note: notes,
    Tab: tabs,
    InvestmentAccount: investmentAccounts,
    InvestmentSnapshot: investmentSnapshots,
    MonzoApiTransaction: monzoApiTransactions,
    AmexTransaction: amexTransactions,
    BarclaysTransaction: barclaysTransactions,
    SantanderTransaction: santanderTransactions,
    HsbcTransaction: hsbcTransactions,
    ChaseTransaction: chaseTransactions,
    SofiTransaction: sofiTransactions,
    PlaidItem: plaidItems,
    PlaidTransaction: plaidTransactions,
  };

  const outPath = join(import.meta.dirname, "../../../restore-data.json");
  writeFileSync(outPath, JSON.stringify(data, null, 2));

  console.log("\nExported:");
  for (const [table, rows] of Object.entries(data)) {
    console.log(`  ${table}: ${(rows as unknown[]).length} rows`);
  }
  console.log(`\nWritten to: ${outPath}`);
}

main().catch(console.error).finally(() => db.$disconnect());
