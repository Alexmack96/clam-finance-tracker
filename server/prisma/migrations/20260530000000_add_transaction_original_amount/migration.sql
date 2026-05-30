-- Adds optional original-currency tracking to Transaction so we can record
-- the pre-conversion USD value for SoFi/Chase rows and keep the converted
-- GBP figure in `amount`. Used as the idempotency marker by the USD→GBP
-- backfill: a row with originalAmount set has already been converted.
ALTER TABLE "Transaction" ADD COLUMN "originalAmount" DECIMAL;
ALTER TABLE "Transaction" ADD COLUMN "originalCurrency" TEXT;
