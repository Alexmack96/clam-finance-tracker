DROP TABLE IF EXISTS "truelayer_transaction";
DROP TABLE IF EXISTS "truelayer_credential";

CREATE TABLE "gocardless_requisition" (
  "id"              TEXT NOT NULL PRIMARY KEY,
  "userId"          TEXT NOT NULL UNIQUE,
  "requisitionId"   TEXT NOT NULL UNIQUE,
  "accountId"       TEXT,
  "accessToken"     TEXT NOT NULL,
  "accessExpiry"    DATETIME NOT NULL,
  "refreshToken"    TEXT NOT NULL,
  "refreshExpiry"   DATETIME NOT NULL,
  "createdAt"       DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "updatedAt"       DATETIME NOT NULL
);

CREATE TABLE "gocardless_transaction" (
  "id"            TEXT NOT NULL PRIMARY KEY,
  "transactionId" TEXT NOT NULL UNIQUE,
  "bookingDate"   TEXT NOT NULL,
  "amount"        TEXT NOT NULL,
  "currency"      TEXT NOT NULL,
  "description"   TEXT NOT NULL,
  "owner"         TEXT NOT NULL DEFAULT 'Alex',
  "importedAt"    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "status"        TEXT NOT NULL DEFAULT 'pending'
);
