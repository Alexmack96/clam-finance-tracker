-- CreateTable
CREATE TABLE "monzo_rec_run" (
    "id" TEXT NOT NULL PRIMARY KEY,
    "ranAt" DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "window" TEXT NOT NULL DEFAULT '90d',
    "trigger" TEXT NOT NULL DEFAULT 'sync',
    "totalMissing" INTEGER NOT NULL DEFAULT 0,
    "results" TEXT NOT NULL
);
