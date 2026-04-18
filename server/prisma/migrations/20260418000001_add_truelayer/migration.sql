CREATE TABLE "truelayer_credential" (
  "id"           TEXT NOT NULL PRIMARY KEY,
  "userId"       TEXT NOT NULL UNIQUE,
  "accessToken"  TEXT NOT NULL,
  "refreshToken" TEXT NOT NULL,
  "expiresAt"    DATETIME NOT NULL,
  "accountId"    TEXT,
  "createdAt"    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "updatedAt"    DATETIME NOT NULL
);

CREATE TABLE "truelayer_transaction" (
  "id"              TEXT NOT NULL PRIMARY KEY,
  "transactionId"   TEXT NOT NULL UNIQUE,
  "timestamp"       DATETIME NOT NULL,
  "description"     TEXT NOT NULL,
  "amount"          REAL NOT NULL,
  "currency"        TEXT NOT NULL,
  "transactionType" TEXT NOT NULL,
  "merchantName"    TEXT,
  "accountId"       TEXT NOT NULL,
  "owner"           TEXT NOT NULL DEFAULT 'Alex',
  "importedAt"      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "status"          TEXT NOT NULL DEFAULT 'pending'
);
