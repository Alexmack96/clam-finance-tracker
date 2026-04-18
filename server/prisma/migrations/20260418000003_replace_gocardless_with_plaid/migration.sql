DROP TABLE IF EXISTS "gocardless_transaction";
DROP TABLE IF EXISTS "gocardless_requisition";

CREATE TABLE "plaid_item" (
  "id"          TEXT NOT NULL PRIMARY KEY,
  "userId"      TEXT NOT NULL UNIQUE,
  "accessToken" TEXT NOT NULL,
  "itemId"      TEXT NOT NULL UNIQUE,
  "cursor"      TEXT,
  "createdAt"   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "updatedAt"   DATETIME NOT NULL
);

CREATE TABLE "plaid_transaction" (
  "id"            TEXT NOT NULL PRIMARY KEY,
  "transactionId" TEXT NOT NULL UNIQUE,
  "date"          TEXT NOT NULL,
  "description"   TEXT NOT NULL,
  "amount"        REAL NOT NULL,
  "currency"      TEXT NOT NULL,
  "merchantName"  TEXT,
  "owner"         TEXT NOT NULL DEFAULT 'Alex',
  "importedAt"    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "status"        TEXT NOT NULL DEFAULT 'pending'
);
