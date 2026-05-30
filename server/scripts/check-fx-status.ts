import { config } from "dotenv";
import { resolve } from "path";
import { fileURLToPath } from "url";
const here = fileURLToPath(new URL(".", import.meta.url));
config({ path: resolve(here, "../../.env") });
const { db } = await import("../src/db/client.js");

const tx = await db.transaction.findMany({
  select: { externalId: true, originalCurrency: true, amount: true, originalAmount: true, date: true, description: true },
});

const source = (ext: string | null) => {
  if (!ext) return "manual";
  if (ext.startsWith("santander-plaid:")) return "santander-plaid";
  return ext.split(":")[0];
};

const buckets: Record<string, { total: number; fxDone: number; noOriginal: number; sumGbp: number; sumUsd: number }> = {};
for (const r of tx) {
  const k = source(r.externalId);
  buckets[k] ??= { total: 0, fxDone: 0, noOriginal: 0, sumGbp: 0, sumUsd: 0 };
  buckets[k].total++;
  if (r.originalCurrency === "USD") buckets[k].fxDone++;
  if (r.originalCurrency === null) buckets[k].noOriginal++;
  buckets[k].sumGbp += Number(r.amount);
  buckets[k].sumUsd += Number(r.originalAmount ?? 0);
}

console.log("Transaction table by source:");
for (const [k, v] of Object.entries(buckets).sort()) {
  console.log(`  ${k.padEnd(18)}  total=${String(v.total).padStart(4)}  fx_done=${String(v.fxDone).padStart(4)}  no_original=${String(v.noOriginal).padStart(4)}  £${v.sumGbp.toFixed(2)}  $${v.sumUsd.toFixed(2)}`);
}

const sofiStaging = await db.sofiTransaction.groupBy({ by: ["status", "owner"], _count: true });
const chaseStaging = await db.chaseTransaction.groupBy({ by: ["status", "owner"], _count: true });
console.log("\nSoFi staging:", sofiStaging);
console.log("Chase staging:", chaseStaging);

process.exit(0);
