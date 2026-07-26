-- Statement files: the source PDF behind a batch of staged rows.
CREATE TABLE "statement_file" (
    "id" TEXT NOT NULL PRIMARY KEY,
    "bank" TEXT NOT NULL,
    "owner" TEXT NOT NULL,
    "statementDate" TEXT,
    "originalName" TEXT NOT NULL,
    "contentHash" TEXT NOT NULL,
    "byteSize" INTEGER NOT NULL,
    "storageKey" TEXT NOT NULL,
    "uploadedAt" DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "rowCount" INTEGER,
    "reconciled" BOOLEAN NOT NULL DEFAULT false
);

CREATE UNIQUE INDEX "statement_file_contentHash_key" ON "statement_file"("contentHash");
CREATE INDEX "statement_file_bank_owner_idx" ON "statement_file"("bank", "owner");

-- Nullable so the 946 rows staged before statement tracking existed stay valid;
-- they simply have no source file to re-parse or download.
ALTER TABLE "amex_transaction" ADD COLUMN "statementFileId" TEXT REFERENCES "statement_file"("id") ON DELETE CASCADE;

CREATE INDEX "amex_transaction_statementFileId_idx" ON "amex_transaction"("statementFileId");
