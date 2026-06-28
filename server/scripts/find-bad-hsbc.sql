-- Investigate bad HSBC imports (e.g. an Amex statement uploaded to the HSBC card).
-- Run against the prod DB (or a prod.db snapshot in DataGrip).

-- 1) Staged HSBC rows, newest first. The bogus batch sits at the top — look for an
--    importedAt that matches when you did the bad upload, plus descriptions/dates that
--    clearly aren't HSBC (Amex merchant names, odd statementDate, etc.).
SELECT
  transactionId, date, paymentType, description,
  moneyIn, moneyOut, balance, statementDate, owner, status, importedAt
FROM hsbc_transaction
ORDER BY importedAt DESC
LIMIT 50;

-- 2) Transactions already PROCESSED from HSBC staging (externalId = 'hsbc:<transactionId>').
--    These only exist if you clicked "Process staged" after the bad upload.
SELECT
  id, date, description, amount, type, owner, externalId, createdAt
FROM "Transaction"
WHERE externalId LIKE 'hsbc:%'
ORDER BY createdAt DESC
LIMIT 50;

-- 3) DELETE — only after you've confirmed the two bad transactionIds above.
--    Replace the ids. Delete the processed Transaction(s) first, then the staging rows.
--    (Or use scripts/delete-bad-hsbc.ts, which does this with a dry-run by default.)
-- DELETE FROM "Transaction"   WHERE externalId   IN ('hsbc:REPLACE_ID_1', 'hsbc:REPLACE_ID_2');
-- DELETE FROM hsbc_transaction WHERE transactionId IN ('REPLACE_ID_1',      'REPLACE_ID_2');
