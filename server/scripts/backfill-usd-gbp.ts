// One-shot CLI version of POST /api/admin/backfill/usd-gbp.
// Usage:
//   cd server && bun run scripts/backfill-usd-gbp.ts          # dry run
//   cd server && bun run scripts/backfill-usd-gbp.ts --apply  # commit changes
import { config } from "dotenv";
import { resolve } from "path";
import { fileURLToPath } from "url";
const here = fileURLToPath(new URL(".", import.meta.url));
config({ path: resolve(here, "../../.env") });
const { db } = await import("../src/db/client.js");
const { convert } = await import("../src/lib/fxRates.js");

const apply = process.argv.includes("--apply");

const rows = await db.transaction.findMany({
  where: {
    originalAmount: null,
    OR: [
      { externalId: { startsWith: "sofi:" } },
      { externalId: { startsWith: "chase:" } },
    ],
  },
  select: { id: true, amount: true, date: true, externalId: true, description: true },
});

console.log(`${apply ? "Applying" : "Dry run"}: ${rows.length} candidate rows`);

let converted = 0;
let errored = 0;
let totalUsd = 0;
let totalGbp = 0;

for (const row of rows) {
  try {
    const usd = Number(row.amount);
    const gbp = await convert(usd, "USD", "GBP", row.date);
    totalUsd += usd;
    totalGbp += gbp;
    if (apply) {
      await db.transaction.update({
        where: { id: row.id },
        data: { amount: gbp, originalAmount: usd, originalCurrency: "USD" },
      });
    } else if (converted < 5) {
      console.log(`  ${row.externalId}  ${row.date.toISOString().slice(0,10)}  $${usd.toFixed(2)} → £${gbp.toFixed(2)}  (${row.description.slice(0,40)})`);
    }
    converted++;
  } catch (err) {
    errored++;
    console.error(`  ERR ${row.externalId}: ${err instanceof Error ? err.message : String(err)}`);
  }
}

console.log(`Done: converted=${converted}  errored=${errored}  total $${totalUsd.toFixed(2)} → £${totalGbp.toFixed(2)}`);
process.exit(0);
