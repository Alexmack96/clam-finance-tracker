import { Router } from "express";
import { db } from "../db/client.js";
import { statementStore } from "../lib/statementStorage.js";
import { parseAmexBuffer, stageAmexRows } from "./import.js";

export const statementsRouter = Router();

// Per-statement management. Before this existed, correcting a bad import meant
// hand-writing DELETE statements against the production volume — the staged rows
// were identifiable only by a `statementDate` string, and the Transaction rows
// they produced only by an `externalId` prefix. A statement is now one row you can
// list, download, re-parse and delete.

/// Staged rows are linked by foreign key; the Transaction rows they produced are
/// linked only by `externalId` (`amex:<transactionId>`), so they have to be
/// matched explicitly.
const EXTERNAL_ID_PREFIX: Record<string, string> = { amex: "amex:" };

async function externalIdsFor(statementFileId: string, bank: string): Promise<string[]> {
  const prefix = EXTERNAL_ID_PREFIX[bank];
  if (!prefix) return [];
  const rows = await db.amexTransaction.findMany({
    where: { statementFileId },
    select: { transactionId: true },
  });
  return rows.map((r) => `${prefix}${r.transactionId}`);
}

// ─── GET /api/admin/statements ───────────────────────────────────────────────

statementsRouter.get("/", async (_req, res) => {
  const files = await db.statementFile.findMany({
    orderBy: [{ statementDate: "desc" }, { uploadedAt: "desc" }],
    include: { _count: { select: { amexRows: true } } },
  });
  res.json(files.map(({ _count, ...f }) => ({ ...f, stagedRows: _count.amexRows })));
});

// ─── GET /api/admin/statements/:id ───────────────────────────────────────────

statementsRouter.get("/:id", async (req, res) => {
  const file = await db.statementFile.findUnique({
    where: { id: req.params.id },
    include: { amexRows: { orderBy: [{ transactionDate: "asc" }, { description: "asc" }] } },
  });
  if (!file) {
    res.status(404).json({ error: "Statement not found" });
    return;
  }

  const externalIds = await externalIdsFor(file.id, file.bank);
  const transactions = await db.transaction.count({ where: { externalId: { in: externalIds } } });

  const { amexRows, ...rest } = file;
  res.json({ ...rest, stagedRows: amexRows.length, transactions, rows: amexRows });
});

// ─── GET /api/admin/statements/:id/file ──────────────────────────────────────

statementsRouter.get("/:id/file", async (req, res) => {
  const file = await db.statementFile.findUnique({ where: { id: req.params.id } });
  if (!file) {
    res.status(404).json({ error: "Statement not found" });
    return;
  }

  let pdf: Buffer;
  try {
    pdf = await statementStore.read(file.storageKey);
  } catch {
    res.status(410).json({ error: "The stored PDF for this statement is missing from the volume" });
    return;
  }

  res.setHeader("Content-Type", "application/pdf");
  // `attachment` (never `inline`) so a PDF can't be rendered in the app's origin.
  res.setHeader(
    "Content-Disposition",
    `attachment; filename="${encodeURIComponent(file.originalName)}"`,
  );
  res.send(pdf);
});

// ─── POST /api/admin/statements/:id/reparse ──────────────────────────────────
//
// Re-run the current parser over the stored bytes. This is the payoff for keeping
// the PDFs: when a parser bug is fixed, correcting a statement no longer means
// hunting down the original file and re-uploading it.

statementsRouter.post("/:id/reparse", async (req, res) => {
  const file = await db.statementFile.findUnique({ where: { id: req.params.id } });
  if (!file) {
    res.status(404).json({ error: "Statement not found" });
    return;
  }
  if (file.bank !== "amex") {
    res.status(400).json({ error: `Re-parse is not supported for ${file.bank} statements yet` });
    return;
  }

  let pdf: Buffer;
  try {
    pdf = await statementStore.read(file.storageKey);
  } catch {
    res.status(410).json({ error: "The stored PDF for this statement is missing from the volume" });
    return;
  }

  // Parse BEFORE deleting anything: a statement that no longer parses must leave
  // the existing rows untouched rather than destroying them for nothing.
  const parsed = await parseAmexBuffer(pdf);
  if (!parsed.ok) {
    res.status(parsed.status).json({ error: parsed.error });
    return;
  }

  const externalIds = await externalIdsFor(file.id, file.bank);
  const removedTransactions = await db.transaction.deleteMany({
    where: { externalId: { in: externalIds } },
  });
  const removedStaged = await db.amexTransaction.deleteMany({
    where: { statementFileId: file.id },
  });

  const staged = await stageAmexRows(parsed.rows, file.owner, file.id);
  await db.statementFile.update({
    where: { id: file.id },
    data: { rowCount: parsed.rows.length, statementDate: parsed.statementDate, reconciled: true },
  });

  res.json({
    removed: { staged: removedStaged.count, transactions: removedTransactions.count },
    ...staged,
  });
});

// ─── DELETE /api/admin/statements/:id ────────────────────────────────────────

statementsRouter.delete("/:id", async (req, res) => {
  const file = await db.statementFile.findUnique({ where: { id: req.params.id } });
  if (!file) {
    res.status(404).json({ error: "Statement not found" });
    return;
  }

  const externalIds = await externalIdsFor(file.id, file.bank);

  // Deleted explicitly rather than relying on the FK cascade: SQLite only honours
  // ON DELETE CASCADE when foreign-key enforcement is on, and the Transaction rows
  // have no foreign key at all. All three go in one transaction so a failure can't
  // strip the transactions but leave the statement looking intact.
  const [removedTransactions, removedStaged] = await db.$transaction([
    db.transaction.deleteMany({ where: { externalId: { in: externalIds } } }),
    db.amexTransaction.deleteMany({ where: { statementFileId: file.id } }),
    db.statementFile.delete({ where: { id: file.id } }),
  ]);
  // After the commit: if unlinking fails the database is already consistent, and
  // an orphaned file on the volume is harmless.
  await statementStore.remove(file.storageKey);

  res.json({
    deleted: {
      statement: file.originalName,
      staged: removedStaged.count,
      transactions: removedTransactions.count,
    },
  });
});
