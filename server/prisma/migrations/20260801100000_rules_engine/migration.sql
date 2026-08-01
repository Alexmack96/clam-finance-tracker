-- Rules engine: ordered, multi-condition Category + Bucket rules.
--
-- Replaces CategoryRule (single pattern, implicit "contains", implicit
-- precedence) and Category.bucket (hidden per-category default) with one
-- ordered Rule list per kind. Behaviour is preserved exactly through the
-- migration: patterns become Contains conditions, positions are seeded from
-- today's resolution order, and every Category.bucket mapping becomes an
-- explicit Bucket rule.

-- ─── Tables ──────────────────────────────────────────────────────────────────

CREATE TABLE "Rule" (
    "id"           TEXT NOT NULL PRIMARY KEY,
    "kind"         TEXT NOT NULL,
    "position"     INTEGER NOT NULL,
    "joinOperator" TEXT NOT NULL DEFAULT 'AND',
    "bank"         TEXT,
    "categoryId"   TEXT,
    "bucket"       TEXT,
    "createdAt"    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "Rule_categoryId_fkey" FOREIGN KEY ("categoryId") REFERENCES "Category" ("id") ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE INDEX "Rule_kind_position_idx" ON "Rule"("kind", "position");

CREATE TABLE "RuleCondition" (
    "id"       TEXT NOT NULL PRIMARY KEY,
    "ruleId"   TEXT NOT NULL,
    "field"    TEXT NOT NULL DEFAULT 'Description',
    "operator" TEXT NOT NULL DEFAULT 'Contains',
    "value"    TEXT NOT NULL,
    "negate"   BOOLEAN NOT NULL DEFAULT false,
    "position" INTEGER NOT NULL,
    CONSTRAINT "RuleCondition_ruleId_fkey" FOREIGN KEY ("ruleId") REFERENCES "Rule" ("id") ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE INDEX "RuleCondition_ruleId_idx" ON "RuleCondition"("ruleId");

-- ─── Migrate CategoryRule → Rule (kind = Category) ───────────────────────────
--
-- position reproduces today's resolution order exactly: bank-specific rules
-- were checked before any-bank rules (resolveRuleCategory), then createdAt.
-- After this migration that ordering is data, not code — the user owns it.

INSERT INTO "Rule" ("id", "kind", "position", "joinOperator", "bank", "categoryId", "bucket", "createdAt")
SELECT
    "id",
    'Category',
    ROW_NUMBER() OVER (ORDER BY CASE WHEN "bank" IS NULL THEN 1 ELSE 0 END, "createdAt") - 1,
    'AND',
    "bank",
    "categoryId",
    NULL,
    "createdAt"
FROM "CategoryRule";

-- Every existing pattern was matched with an implicit "*pattern*", so a leading
-- or trailing "*" was decorative and matched the empty string. Stripping it and
-- mapping to Contains is behaviour-preserving. Mapping a trailing "*" to
-- StartsWith would NOT be — it would silently un-match live transactions.

INSERT INTO "RuleCondition" ("id", "ruleId", "field", "operator", "value", "negate", "position")
SELECT
    lower(hex(randomblob(12))),
    "id",
    'Description',
    'Contains',
    TRIM("pattern", '*'),
    false,
    0
FROM "CategoryRule";

-- ─── Seed Bucket rules from Category.bucket ──────────────────────────────────
--
-- Seeded literally, so the first import after this behaves identically. Several
-- of these mappings are known to be wrong (Salary → Needs, Uncategorised →
-- Wants, Groceries → Wants); they are seeded anyway so nothing shifts silently,
-- and corrected via the dry-run on the Rules page.
--
-- Conditions match on category NAME rather than id so that Contains/StartsWith
-- are meaningful ("category contains Sauce"). Renaming a category rewrites
-- matching Exact conditions (see routes/categories.ts).

INSERT INTO "Rule" ("id", "kind", "position", "joinOperator", "bank", "categoryId", "bucket", "createdAt")
SELECT
    lower(hex(randomblob(12))),
    'Bucket',
    ROW_NUMBER() OVER (ORDER BY "name") - 1,
    'AND',
    NULL,
    NULL,
    "bucket",
    CURRENT_TIMESTAMP
FROM "Category"
WHERE "bucket" IS NOT NULL;

INSERT INTO "RuleCondition" ("id", "ruleId", "field", "operator", "value", "negate", "position")
SELECT
    lower(hex(randomblob(12))),
    r."id",
    'Category',
    'Exact',
    c."name",
    false,
    0
FROM "Rule" r
JOIN "Category" c
  ON c."bucket" IS NOT NULL
 AND r."kind" = 'Bucket'
 AND r."position" = (
       SELECT COUNT(*) FROM "Category" c2
       WHERE c2."bucket" IS NOT NULL AND c2."name" < c."name"
     );

-- ─── Transaction pins ────────────────────────────────────────────────────────
--
-- Default false everywhere: provenance was never recorded, so no truthful
-- backfill exists. The supervised dry-run immediately after this migration is
-- the one chance to catch a rule stamping over past hand-work.

ALTER TABLE "Transaction" ADD COLUMN "categoryPinned" BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE "Transaction" ADD COLUMN "bucketPinned"   BOOLEAN NOT NULL DEFAULT false;

-- ─── Drop the replaced structures ────────────────────────────────────────────

DROP TABLE "CategoryRule";
ALTER TABLE "Category" DROP COLUMN "bucket";
