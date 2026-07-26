-- Reconciliation is now self-healing: a run backfills the gaps it finds into
-- monzo_api_transaction. Track how many rows a run actually recovered, so a run
-- record distinguishes "found 16 gaps, recovered all 16" from "found 16, recovered 0".
ALTER TABLE "monzo_rec_run" ADD COLUMN "totalBackfilled" INTEGER NOT NULL DEFAULT 0;
