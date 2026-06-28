-- Introduce SavingType (Fixed/Fun/Saving) as the classification, replacing Category.isFixed.
-- Category carries the default; a Transaction can override it (null = inherit).

-- Category: add savingType, backfill from isFixed (and known savings categories), drop old flags.
ALTER TABLE "Category" ADD COLUMN "savingType" TEXT NOT NULL DEFAULT 'Fun';

UPDATE "Category" SET "savingType" = CASE
  WHEN "name" IN ('Savings', 'Investments') THEN 'Saving'
  WHEN "isFixed" = 1 THEN 'Fixed'
  ELSE 'Fun'
END;

ALTER TABLE "Category" DROP COLUMN "isFixed";
ALTER TABLE "Category" DROP COLUMN "isDirectDebit";

-- Transaction: nullable per-transaction override (null = inherit the category's savingType).
ALTER TABLE "Transaction" ADD COLUMN "savingType" TEXT;
