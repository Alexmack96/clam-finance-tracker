-- Make investment accounts per-owner. Existing rows belong to Alex.
ALTER TABLE "investment_account" ADD COLUMN "owner" TEXT NOT NULL DEFAULT 'Alex';

-- Account names are unique per owner, not globally (Casey can have her own "Pension").
DROP INDEX "investment_account_name_key";
CREATE UNIQUE INDEX "investment_account_owner_name_key" ON "investment_account"("owner", "name");
