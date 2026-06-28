-- Casey Amex duplicate check. Run against prod (or the prod.db snapshot in DataGrip).
-- The Amex importer appends "-N" to the transactionId when the same content (date|desc|
-- amount|isCredit) appears more than once within a single upload, so a true duplicate
-- shows up as a base id + a "<base>-1" id with identical details.

-- 1) All Casey Amex content-duplicate groups (staging), showing the paired ids.
SELECT
  transactionDate,
  description,
  amount,
  isCredit,
  COUNT(*)                               AS copies,
  GROUP_CONCAT(transactionId, '  |  ')   AS transactionIds,
  GROUP_CONCAT(status, ', ')             AS statuses
FROM amex_transaction
WHERE owner = 'Casey'
GROUP BY transactionDate, description, amount, isCredit
HAVING COUNT(*) > 1
ORDER BY transactionDate;

-- 2) Inspect two specific ids side by side — staging row + processed Transaction.
--    Replace the ids with the two you want to check.
SELECT 'staging' AS source, a.transactionId AS id, a.transactionDate AS date,
       a.description, a.amount, a.isCredit, a.status, NULL AS externalId
FROM amex_transaction a
WHERE a.transactionId IN ('REPLACE_ID_1', 'REPLACE_ID_2')
UNION ALL
SELECT 'transaction' AS source, NULL, date(t.date),
       t.description, CAST(t.amount AS TEXT), NULL, NULL, t.externalId
FROM "Transaction" t
WHERE t.externalId IN ('amex:REPLACE_ID_1', 'amex:REPLACE_ID_2');

-- 3) The "extra" copies only (every "<base>-N" suffixed id) — the deletion candidates.
--    Base ids are pure hex, so any id containing a hyphen is a within-batch duplicate suffix.
SELECT transactionId, transactionDate, description, amount, status
FROM amex_transaction
WHERE owner = 'Casey' AND transactionId LIKE '%-%'
ORDER BY transactionDate;
