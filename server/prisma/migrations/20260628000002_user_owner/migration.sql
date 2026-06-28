-- Tie each login to the person (Alex/Casey) it represents, so pages can default to the
-- logged-in user. Backfill the two known accounts by email.
ALTER TABLE "user" ADD COLUMN "owner" TEXT;

UPDATE "user" SET "owner" = 'Alex'  WHERE "email" = 'alexmackintosh96@gmail.com';
UPDATE "user" SET "owner" = 'Casey' WHERE "email" = 'caseyliddy794@gmail.com';
