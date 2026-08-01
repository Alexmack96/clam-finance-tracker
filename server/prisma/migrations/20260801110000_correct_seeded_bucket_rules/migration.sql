-- Corrections to the rules seeded by 20260801100000_rules_engine.
--
-- That migration seeded Bucket rules *literally* from the old Category.bucket
-- column so nothing shifted silently. This one fixes the mappings that are
-- provably wrong, so a prod deploy lands in the same corrected state as dev
-- rather than needing the same edits made twice by hand.
--
-- Every statement is data-driven and guarded: on a database where the condition
-- does not hold, it is a no-op rather than damage.

-- ─── 1. Protect hand-set Ignore ──────────────────────────────────────────────
--
-- No Category ever mapped to Ignore, so no seeded Bucket rule can assign it —
-- which makes `bucket = 'Ignore'` provable evidence of a manual choice, not a
-- guess. Pin those rows before any rule run can claim them. The NOT EXISTS
-- guard keeps this honest: if some Rule does assign Ignore, the inference no
-- longer holds and nothing is pinned.

UPDATE "Transaction"
SET "bucketPinned" = true
WHERE "bucket" = 'Ignore'
  AND NOT EXISTS (
    SELECT 1 FROM "Rule" WHERE "kind" = 'Bucket' AND "bucket" = 'Ignore'
  );

-- ─── 2. Salary is income, not a Need ─────────────────────────────────────────
--
-- Category.bucket had Salary → Needs. Harmless while income was hardcoded to
-- bucket: null, but 20260801100000 removed that gate so refunds could net —
-- which would now stamp salary as Needs and wreck the 50/30/20 split.
--
-- Salary stays Uncategorised and counts as real income (see CONTEXT.md), so the
-- rule is deleted rather than rewritten. Only a rule whose *single* condition is
-- an exact match on Salary is touched; anything hand-built with more conditions
-- is left alone.

DELETE FROM "RuleCondition"
WHERE "ruleId" IN (
  SELECT r."id" FROM "Rule" r
  JOIN "RuleCondition" c ON c."ruleId" = r."id"
  WHERE r."kind" = 'Bucket'
    AND c."field" = 'Category' AND c."operator" = 'Exact' AND c."value" = 'Salary'
  GROUP BY r."id" HAVING COUNT(*) = 1
);

DELETE FROM "Rule"
WHERE "kind" = 'Bucket'
  AND NOT EXISTS (SELECT 1 FROM "RuleCondition" WHERE "ruleId" = "Rule"."id");

-- ─── 3. Unmatched transactions stay Uncategorised ────────────────────────────
--
-- Category.bucket had Uncategorised → Wants, so anything no rule claimed
-- silently inflated Wants instead of showing up as unclassified. That is the
-- opposite of what Uncategorised means. 21 of 25 categories mapped to Wants and
-- the result was 1150 Wants against 472 Needs; this is the biggest single
-- contributor.

DELETE FROM "RuleCondition"
WHERE "ruleId" IN (
  SELECT r."id" FROM "Rule" r
  JOIN "RuleCondition" c ON c."ruleId" = r."id"
  WHERE r."kind" = 'Bucket'
    AND c."field" = 'Category' AND c."operator" = 'Exact' AND c."value" = 'Uncategorised'
  GROUP BY r."id" HAVING COUNT(*) = 1
);

DELETE FROM "Rule"
WHERE "kind" = 'Bucket'
  AND NOT EXISTS (SELECT 1 FROM "RuleCondition" WHERE "ruleId" = "Rule"."id");

-- ─── 4. Repoint the two rules that have never fired ──────────────────────────
--
-- "ALEXANDER MACKINTO wifi/rent" were scoped to monzo, but every
-- "ALEXANDER MACKINTO …" description is on hsbc, so they have matched nothing
-- since the day they were written. The NOT EXISTS guard means this can only
-- ever improve matters: it runs solely when no monzo/flex transaction carries
-- that description, in which case the rules match nothing either way.

UPDATE "Rule"
SET "bank" = 'hsbc'
WHERE "bank" = 'monzo'
  AND "id" IN (
    SELECT "ruleId" FROM "RuleCondition"
    WHERE "field" = 'Description' AND "value" LIKE 'ALEXANDER MACKINTO%'
  )
  AND NOT EXISTS (
    SELECT 1 FROM "Transaction"
    WHERE "description" LIKE '%ALEXANDER MACKINTO%'
      AND ("externalId" LIKE 'monzo:%' OR "externalId" LIKE 'flex:%')
  );

-- ─── 5. Close the gaps left by the deletions ─────────────────────────────────
--
-- Sparse positions still resolve correctly — precedence is relative — but they
-- make the reorder payload and the "#3" labels in the UI confusing to read.
-- Positions are unique within a kind, so counting predecessors re-densifies
-- them without changing any relative order.

UPDATE "Rule"
SET "position" = (
  SELECT COUNT(*) FROM "Rule" r2
  WHERE r2."kind" = "Rule"."kind" AND r2."position" < "Rule"."position"
);
