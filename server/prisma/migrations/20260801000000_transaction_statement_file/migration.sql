-- Link a normalised Transaction directly to the statement PDF it came from.
--
-- Until now the only link was `externalId = '<bank>:' || <staging id>` — a string
-- concat, so unindexed, and different for every bank. Answering "which statement
-- produced this transaction?" meant a UNION across every staging table. A real
-- column makes it one join, lets SQLite index it, and lets deletes cascade.
--
-- ON DELETE SET NULL, not CASCADE: deleting a statement should orphan the derived
-- transactions, never silently destroy them. The delete route removes them on
-- purpose, which stays an explicit decision rather than a schema side effect.
ALTER TABLE "Transaction" ADD COLUMN "statementFileId" TEXT REFERENCES "statement_file"("id") ON DELETE SET NULL;

CREATE INDEX "Transaction_statementFileId_idx" ON "Transaction"("statementFileId");

-- Backfill through the externalId string join this column replaces. Only Amex has
-- statement files today; every other bank's rows stay NULL until it gets the same
-- treatment.
UPDATE "Transaction"
SET "statementFileId" = (
  SELECT a."statementFileId"
  FROM "amex_transaction" a
  WHERE 'amex:' || a."transactionId" = "Transaction"."externalId"
)
WHERE "externalId" LIKE 'amex:%';
