-- Remove poisoned HSBC staging rows created by uploading a non-HSBC (Amex) statement to the
-- HSBC import card before the upload guard existed. The HSBC parser couldn't find structure
-- in an Amex PDF and produced giant rows with an empty date, which crashed /process
-- (new Date("") → Invalid Date → PrismaClientValidationError). Legit HSBC rows always carry a
-- parsed YYYY-MM-DD date, so this only removes the junk. They were never processed, so there
-- are no Transaction rows to clean up.
DELETE FROM "hsbc_transaction" WHERE date IS NULL OR trim(date) = '';
