-- Add a content-hash business key to the Barclays and Santander staging tables.
-- Nullable so the column can be added to already-populated tables; existing rows
-- are backfilled by scripts/cleanup-and-backfill.ts on deploy. SQLite permits
-- multiple NULLs under a UNIQUE index, so this is safe pre-backfill.

ALTER TABLE "barclays_transaction" ADD COLUMN "transactionId" TEXT;
CREATE UNIQUE INDEX "barclays_transaction_transactionId_key" ON "barclays_transaction"("transactionId");

ALTER TABLE "santander_transaction" ADD COLUMN "transactionId" TEXT;
CREATE UNIQUE INDEX "santander_transaction_transactionId_key" ON "santander_transaction"("transactionId");
