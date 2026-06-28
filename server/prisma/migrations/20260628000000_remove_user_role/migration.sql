-- Drop the vestigial Admin/User role concept.
ALTER TABLE "user" DROP COLUMN "role";
