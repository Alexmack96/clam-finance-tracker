-- Make accountId nullable
ALTER TABLE monzo_credential RENAME TO monzo_credential_old;
CREATE TABLE monzo_credential (
  id           TEXT NOT NULL PRIMARY KEY,
  userId       TEXT NOT NULL UNIQUE,
  accessToken  TEXT NOT NULL,
  refreshToken TEXT NOT NULL,
  expiresAt    DATETIME NOT NULL,
  accountId    TEXT,
  createdAt    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updatedAt    DATETIME NOT NULL
);
INSERT INTO monzo_credential SELECT * FROM monzo_credential_old;
DROP TABLE monzo_credential_old;
