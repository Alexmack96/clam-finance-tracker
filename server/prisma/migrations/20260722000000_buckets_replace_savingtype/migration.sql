-- Replace SavingType (Fixed/Fun/Saving) + excludeFromSavings with a single nullable
-- Bucket (Needs/Wants/Savings/Ignore) on both Category and Transaction.
-- The Transaction Bucket is the source of truth; the Category Bucket is a mapping.

-- ── Add nullable Bucket columns ──────────────────────────────────────────────
ALTER TABLE "Category" ADD COLUMN "bucket" TEXT;
ALTER TABLE "Transaction" ADD COLUMN "bucket" TEXT;

-- ── Backfill Category.bucket from savingType ─────────────────────────────────
UPDATE "Category" SET "bucket" = CASE "savingType"
  WHEN 'Fixed'  THEN 'Needs'
  WHEN 'Fun'    THEN 'Wants'
  WHEN 'Saving' THEN 'Savings'
END;

-- ── Backfill Transaction.bucket ──────────────────────────────────────────────
-- Anything previously excluded becomes Ignore (both income and expense).
UPDATE "Transaction" SET "bucket" = 'Ignore' WHERE "excludeFromSavings" = 1;

-- Expense with a per-transaction override: map the override directly.
UPDATE "Transaction" SET "bucket" = CASE "savingType"
  WHEN 'Fixed'  THEN 'Needs'
  WHEN 'Fun'    THEN 'Wants'
  WHEN 'Saving' THEN 'Savings'
END
WHERE "type" = 'Expense' AND "excludeFromSavings" = 0 AND "savingType" IS NOT NULL;

-- Expense with no override: inherit the category's mapped Bucket (stamped concretely).
UPDATE "Transaction" SET "bucket" = (
  SELECT CASE c."savingType"
    WHEN 'Fixed'  THEN 'Needs'
    WHEN 'Fun'    THEN 'Wants'
    WHEN 'Saving' THEN 'Savings'
  END
  FROM "Category" c WHERE c."id" = "Transaction"."categoryId"
)
WHERE "type" = 'Expense' AND "excludeFromSavings" = 0 AND "savingType" IS NULL;

-- Income stays Uncategorised (null) unless it was excluded (handled above as Ignore),
-- so genuine income keeps counting as real income in the savings score.

-- ── Drop the old machinery ───────────────────────────────────────────────────
ALTER TABLE "Category" DROP COLUMN "savingType";
ALTER TABLE "Transaction" DROP COLUMN "savingType";
ALTER TABLE "Transaction" DROP COLUMN "excludeFromSavings";
