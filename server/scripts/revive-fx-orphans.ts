// Recovery: SoFi/Chase staging rows marked "processed" but with no matching
// Transaction get reset to "pending" so the next /process run can FX-convert
// and re-insert them.
//
// Run: bun run scripts/revive-fx-orphans.ts
//      bun run scripts/revive-fx-orphans.ts --apply
import { config } from "dotenv";
import { resolve } from "path";
import { fileURLToPath } from "url";
const here = fileURLToPath(new URL(".", import.meta.url));
config({ path: resolve(here, "../../.env") });
const { db } = await import("../src/db/client.js");

const apply = process.argv.includes("--apply");

async function findOrphans(bank: "sofi" | "chase") {
  const table = bank === "sofi" ? db.sofiTransaction : db.chaseTransaction;
  const rows = await table.findMany({ where: { status: "processed" }, select: { id: true, transactionId: true } });
  const orphanIds: number[] = [];
  for (const r of rows) {
    const ext = `${bank}:${r.transactionId}`;
    const tx = await db.transaction.findUnique({ where: { externalId: ext }, select: { id: true } });
    if (!tx) orphanIds.push(r.id);
  }
  return orphanIds;
}

const sofiOrphans = await findOrphans("sofi");
const chaseOrphans = await findOrphans("chase");

console.log(`${apply ? "Applying" : "Dry run"}: sofi orphans=${sofiOrphans.length}  chase orphans=${chaseOrphans.length}`);

if (apply) {
  if (sofiOrphans.length > 0) {
    await db.sofiTransaction.updateMany({ where: { id: { in: sofiOrphans } }, data: { status: "pending" } });
    console.log(`  Reset ${sofiOrphans.length} SoFi rows → pending`);
  }
  if (chaseOrphans.length > 0) {
    await db.chaseTransaction.updateMany({ where: { id: { in: chaseOrphans } }, data: { status: "pending" } });
    console.log(`  Reset ${chaseOrphans.length} Chase rows → pending`);
  }
  console.log("Now run POST /api/admin/process (or upload anything via the UI — it auto-processes).");
}

process.exit(0);
