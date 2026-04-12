CREATE TABLE "monzo_credential" (
  "id"           TEXT     NOT NULL PRIMARY KEY,
  "userId"       TEXT     NOT NULL,
  "accessToken"  TEXT     NOT NULL,
  "refreshToken" TEXT     NOT NULL,
  "expiresAt"    DATETIME NOT NULL,
  "accountId"    TEXT     NOT NULL,
  "createdAt"    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "updatedAt"    DATETIME NOT NULL
);

CREATE UNIQUE INDEX "monzo_credential_userId_key" ON "monzo_credential"("userId");
