CREATE TABLE "CategoryRule" (
    "id"         TEXT     NOT NULL PRIMARY KEY,
    "pattern"    TEXT     NOT NULL,
    "bank"       TEXT,
    "categoryId" TEXT     NOT NULL,
    "createdAt"  DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "CategoryRule_categoryId_fkey" FOREIGN KEY ("categoryId") REFERENCES "Category" ("id") ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE UNIQUE INDEX "CategoryRule_pattern_bank_key" ON "CategoryRule"("pattern", "bank");
