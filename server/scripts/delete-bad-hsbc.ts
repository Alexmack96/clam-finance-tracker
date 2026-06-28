// One-shot: delete bad HSBC staging rows (and any Transactions processed from them).
// Caused by uploading a non-HSBC statement (e.g. Amex) to the HSBC import endpoint.
//
// It runs against whatever DATABASE_URL is in ../../.env — point that at prod to clean prod.
// Dry run by default (prints what it WOULD delete); add --apply to actually delete.
//
//   bun scripts/delete-bad-hsbc.ts <hsbcTransactionId> [<hsbcTransactionId> ...]
//   bun scripts/delete-bad-hsbc.ts <id1> <id2> --apply
import { config } from "dotenv";
import { resolve } from "path";
import { fileURLToPath } from "url";
const here = fileURLToPath(new URL(".", import.meta.url));
config({ path: resolve(here, "../../.env") });
const { db } = await import("../src/db/client.js");

const args = process.argv.slice(2);
const apply = args.includes("--apply");
const ids = args.filter((a) => !a.startsWith("--"));

if (ids.length === 0) {
  console.error("Usage: bun scripts/delete-bad-hsbc.ts <hsbcTransactionId...> [--apply]");
  process.exit(1);
}

console.log(`${apply ? "APPLYING" : "DRY RUN"} — ${ids.length} HSBC staging row(s) targeted:`);

for (const id of ids) {
  const staged = await db.hsbcTransaction.findUnique({ where: { transactionId: id } });
  const externalId = `hsbc:${id}`;
  const tx = await db.transaction.findUnique({ where: { externalId } });

  console.log(`\n• ${id}`);
  console.log(
    `   staging row : ${staged ? `${staged.date} "${staged.description}" (status=${staged.status})` : "not found"}`,
  );
  console.log(
    `   transaction : ${tx ? `${tx.description} £${tx.amount} (id=${tx.id})` : "not found / not processed"}`,
  );

  if (apply) {
    // Delete the processed Transaction first (FK-free, but order keeps intent clear).
    if (tx) await db.transaction.delete({ where: { id: tx.id } });
    if (staged) await db.hsbcTransaction.delete({ where: { id: staged.id } });
    console.log("   → deleted");
  }
}

if (!apply) console.log("\nNothing deleted (dry run). Re-run with --apply to delete.");
await db.$disconnect();
