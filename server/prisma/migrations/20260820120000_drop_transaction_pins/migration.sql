-- Rules no longer skip hand-edited fields: a dry-run is what protects a
-- deliberate choice, so the pin flags have nothing left to gate.
ALTER TABLE "Transaction" DROP COLUMN "categoryPinned";
ALTER TABLE "Transaction" DROP COLUMN "bucketPinned";
