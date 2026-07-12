/**
 * One-shot, idempotent deploy task. Runs on every deploy (wired into the
 * Dockerfile CMD, after `prisma migrate deploy` and the seed); safe to re-run.
 *
 * It does four things, in order:
 *
 *   1. CLEANUP — remove the surplus copies a double-uploaded Barclays file created,
 *      KEEPING THE FIRST copy of each real transaction (no data loss, no re-upload).
 *      Per statementDate we group rows by content key. The GCD of the per-key copy
 *      counts is k = how many times the file was uploaded; if k >= 2 we keep the
 *      first count/k copies of each transaction (lowest id) and delete the rest,
 *      along with their processed transactions. Genuine same-day repeats (e.g. four
 *      £1.75 Lime hires) give a GCD of 1, so a legitimately-single upload is never
 *      touched. Each statement's deletion runs inside a DB transaction and is rolled
 *      back if the number of transactions actually deleted does not match the number
 *      of duplicate copies we planned to remove — the deleted count is logged either
 *      way so the result is verifiable from the deploy logs. Idempotent: once a
 *      statement has no duplicate copies left (k == 1) it is skipped.
 *
 *   2. BACKFILL — populate the new `transactionId` business key on every existing
 *      Barclays/Santander staging row, and migrate the matching processed
 *      transaction's externalId from the old `bank:<autoincrement-id>` scheme to
 *      the new `bank:<transactionId>` scheme. Only rows with a NULL transactionId
 *      are touched, so this is also a no-op once complete.
 *
 *   3. FLEX MIGRATE — re-namespace Monzo Flex transactions that were processed
 *      before the Monzo/Flex split existed, from `monzo:<id>` to `flex:<id>`, so
 *      they show as their own card in the UI. Only rows on a non-retail Monzo
 *      account whose Transaction still has the old externalId are touched, so
 *      this is a no-op once complete.
 *
 *   4. CATEGORY CLEANUP — one-off category consolidations that used to run on
 *      every server boot (mapMonzoCategories, consolidateFoodCategories,
 *      migrateTakeout, migrateOwners, defined in ../routes/admin.ts). They only
 *      ever have work to do right after the historical data they target first
 *      appears, so — like everything else here — they belong at deploy time,
 *      not on every local `bun --watch` restart.
 *
 * Cleanup runs BEFORE backfill on purpose: the duplicated transactions still use
 * the old `barclays:<id>` externalId, which is how cleanup finds and deletes them.
 */

import "dotenv/config";
import { createHash } from "crypto";
import { db } from "../db/client.js";
import {
  mapMonzoCategories,
  consolidateFoodCategories,
  migrateTakeout,
  migrateOwners,
} from "../routes/admin.js";

function hash16(s: string): string {
  return createHash("sha256").update(s).digest("hex").slice(0, 16);
}

function gcd(a: number, b: number): number {
  return b === 0 ? a : gcd(b, a % b);
}

// ─── 1. Cleanup duplicated Barclays statements ───────────────────────────────

async function cleanupDuplicatedBarclaysStatements() {
  const rows = await db.barclaysTransaction.findMany({
    select: { id: true, statementDate: true, date: true, description: true, amount: true, isCredit: true },
  });

  const byStatement = new Map<string, typeof rows>();
  for (const r of rows) {
    const list = byStatement.get(r.statementDate) ?? [];
    list.push(r);
    byStatement.set(r.statementDate, list);
  }

  for (const [statementDate, group] of byStatement) {
    // Group this statement's rows by content key so we can keep the first copy of
    // each real transaction and drop the surplus copies a re-upload created.
    const byKey = new Map<string, typeof rows>();
    for (const r of group) {
      const key = `${r.date}|${r.description}|${r.amount}|${r.isCredit}`;
      const list = byKey.get(key) ?? [];
      list.push(r);
      byKey.set(key, list);
    }

    // k = how many times the file was uploaded (GCD of per-key copy counts).
    // k == 1 → no whole-file duplication (genuine same-day repeats are preserved).
    const k = [...byKey.values()].reduce((g, list) => gcd(g, list.length), 0);
    if (k < 2) continue;

    // Keep the first count/k copies of each transaction (lowest id); delete the rest.
    const dupeIds: number[] = [];
    for (const list of byKey.values()) {
      const keep = list.length / k;
      const sorted = [...list].sort((a, b) => a.id - b.id);
      dupeIds.push(...sorted.slice(keep).map((r) => r.id));
    }

    const expected = dupeIds.length;
    const extIds = dupeIds.map((id) => `barclays:${id}`);

    try {
      await db.$transaction(async (tx) => {
        const txResult  = await tx.transaction.deleteMany({ where: { externalId: { in: extIds } } });
        const stgResult = await tx.barclaysTransaction.deleteMany({ where: { id: { in: dupeIds } } });
        console.log(
          `[cleanup] Barclays "${statementDate}": uploaded ${k}×, ${group.length} staged rows. ` +
          `Removing ${expected} duplicate copies (keeping the first of each) → ` +
          `deleted ${txResult.count} transactions and ${stgResult.count} staging rows.`,
        );
        // Safety: every duplicate staging row should map to exactly one processed
        // transaction. If the transaction delete count doesn't match the plan,
        // something is off — throw to roll this statement's deletion back rather
        // than commit a half-correct cleanup.
        if (txResult.count !== expected) {
          throw new Error(
            `expected to delete ${expected} transactions but deleted ${txResult.count} — rolling back "${statementDate}"`,
          );
        }
      });
    } catch (err) {
      console.error(`[cleanup] ROLLED BACK Barclays "${statementDate}":`, err instanceof Error ? err.message : err);
    }
  }
}

// ─── 2. Backfill business keys + migrate transaction externalIds ─────────────

interface BackfillRow { id: number; transactionId: string | null; contentKey: string }

/**
 * Assign a unique `transactionId` to every staging row that lacks one, then point
 * the matching processed transaction at the new externalId. `bank` is the
 * externalId namespace ("barclays" / "santander"). Rows are processed in a stable
 * order and suffixed _1, _2… on collision, so the result is deterministic and the
 * UNIQUE index can never be violated.
 */
async function backfill(
  bank: string,
  load: () => Promise<BackfillRow[]>,
  setKey: (id: number, transactionId: string) => Promise<unknown>,
) {
  const rows = await load();
  const taken = new Set(rows.map((r) => r.transactionId).filter((t): t is string => t !== null));
  const pending = rows
    .filter((r) => r.transactionId === null)
    .sort((a, b) => a.contentKey.localeCompare(b.contentKey) || a.id - b.id);

  let keyed = 0;
  let reExternalised = 0;
  for (const row of pending) {
    const baseId = hash16(row.contentKey);
    let transactionId = baseId;
    let c = 0;
    while (taken.has(transactionId)) transactionId = `${baseId}_${++c}`;
    taken.add(transactionId);

    await setKey(row.id, transactionId);
    keyed++;

    const oldExt = `${bank}:${row.id}`;
    const newExt = `${bank}:${transactionId}`;
    if (oldExt === newExt) continue;
    const tx = await db.transaction.findUnique({ where: { externalId: oldExt }, select: { id: true } });
    if (!tx) continue;
    const clash = await db.transaction.findUnique({ where: { externalId: newExt }, select: { id: true } });
    if (clash) continue; // target already taken — leave the old row as-is rather than collide
    await db.transaction.update({ where: { id: tx.id }, data: { externalId: newExt } });
    reExternalised++;
  }

  if (keyed > 0 || reExternalised > 0) {
    console.log(`[backfill] ${bank}: keyed ${keyed} staging rows, migrated ${reExternalised} transaction externalIds.`);
  }
}

async function backfillBarclays() {
  await backfill(
    "barclays",
    async () => {
      const rows = await db.barclaysTransaction.findMany({
        select: { id: true, transactionId: true, date: true, description: true, amount: true, isCredit: true },
      });
      return rows.map((r) => ({
        id: r.id,
        transactionId: r.transactionId,
        contentKey: `${r.date}|${r.description}|${r.amount}|${r.isCredit}`,
      }));
    },
    (id, transactionId) => db.barclaysTransaction.update({ where: { id }, data: { transactionId } }),
  );
}

async function backfillSantander() {
  await backfill(
    "santander",
    async () => {
      const rows = await db.santanderTransaction.findMany({
        select: { id: true, transactionId: true, date: true, description: true, moneyIn: true, moneyOut: true, balance: true },
      });
      return rows.map((r) => ({
        id: r.id,
        transactionId: r.transactionId,
        contentKey: `${r.date}|${r.description}|${r.moneyIn ?? ""}|${r.moneyOut ?? ""}|${r.balance}`,
      }));
    },
    (id, transactionId) => db.santanderTransaction.update({ where: { id }, data: { transactionId } }),
  );
}

// ─── 3. Re-namespace Monzo Flex transactions ─────────────────────────────────

async function migrateFlexTransactions() {
  const credential = await db.monzoCredential.findFirst({ select: { accountId: true } });
  if (!credential?.accountId) return; // Monzo not connected — nothing to do

  const flexRows = await db.monzoApiTransaction.findMany({
    where: { accountId: { not: credential.accountId } },
    select: { monzoId: true },
  });
  if (flexRows.length === 0) return;

  let migrated = 0;
  let reowned = 0;
  for (const row of flexRows) {
    const oldExt = `monzo:${row.monzoId}`;
    const newExt = `flex:${row.monzoId}`;
    const tx = await db.transaction.findUnique({
      where: { externalId: oldExt },
      select: { id: true, owner: true, reviewed: true },
    });
    if (!tx) continue; // not yet processed, or already migrated

    // Flex now defaults new syncs to owner "Alex" instead of "Joint" — apply the
    // same fix here, but only to rows still at the untouched default so a manual
    // owner edit (reviewed: true, or already re-owned) is never overwritten.
    const reowning = tx.owner === "Joint" && !tx.reviewed;
    await db.transaction.update({
      where: { id: tx.id },
      data: { externalId: newExt, ...(reowning ? { owner: "Alex" } : {}) },
    });
    migrated++;
    if (reowning) reowned++;
  }
  if (migrated > 0) {
    console.log(
      `[flex-migrate] re-namespaced ${migrated} Monzo Flex transaction externalId(s) monzo: → flex: ` +
        `(${reowned} also re-owned Joint → Alex).`,
    );
  }
}

async function main() {
  await cleanupDuplicatedBarclaysStatements();
  await backfillBarclays();
  await backfillSantander();
  await migrateFlexTransactions();
  await consolidateFoodCategories();
  await migrateTakeout();
  await mapMonzoCategories();
  await migrateOwners();
  console.log("[cleanup-and-backfill] done.");
}

// Never block the deploy: log and exit 0 even on failure so the server still starts.
main()
  .catch((err) => console.error("[cleanup-and-backfill] failed (continuing):", err))
  .finally(() => db.$disconnect());
