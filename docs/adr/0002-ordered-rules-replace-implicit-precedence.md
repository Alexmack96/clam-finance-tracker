# 2. Ordered rules replace implicit precedence, and own bucketing

Date: 2026-08-01

## Status

Accepted. Supersedes the Category Bucket mapping decided in
[0001](./0001-buckets-replace-savingtype.md).

## Context

`CategoryRule` was a single description pattern, optionally scoped to a bank,
mapping to a category. Matching was implicit-contains with `*` as a wildcard.
Precedence was implicit: bank-specific rules were checked before any-bank ones,
then whichever the database returned first. Bucketing was separate — a
`Category.bucket` column stamped onto expenses at import.

Four problems, all visible in the production data:

- **No way to express an exception.** `contains "Golf"` got 23 of 26 rows right;
  the failures were `FASTER PAYMENTS RECEIPT REF.Golf ting FROM …` (money from a
  friend) and a staging charge. No positive pattern separates them — "Golf"
  genuinely appears in both — and `exact`/`startsWith`/`endsWith` all made it
  worse, because the merchant name sits mid-string.
- **Precedence was invisible and inconsistent.** The list endpoint sorted by
  `createdAt`; the import path had no `orderBy` at all, so two overlapping rules
  could resolve differently on screen than during a `/process` run.
- **Rules failed silently.** Two rules scoped to `monzo` had never matched
  anything, because every `ALEXANDER MACKINTO …` description is on `hsbc`. They
  looked identical to working rules.
- **Bucketing was a hidden default.** 21 of 25 categories mapped to `Wants`,
  including `Groceries` and `Salary`, producing 1150 Wants against 472 Needs.
  Nobody had chosen that; it was an uncurated column nobody could see.

## Decision

One `Rule` model with an ordered list per kind, and a child `RuleCondition` table.

- **Two kinds, run as a pipeline.** All `Category` rules resolve first, then all
  `Bucket` rules — so a Bucket rule can test the Category a Category rule just
  assigned (`Transport` → Needs, but `Transport` + `uber` → Wants). A single
  merged list was rejected: a rule's conditions would then depend on the output
  of rules above it, making resolution order-of-evaluation dependent.
- **`position` is the whole of precedence.** First match wins. No specificity
  heuristic — with N conditions and negations, "which rule is more specific" has
  no answer a user can predict, and an unpredictable answer is worse than a
  manual one for automation that fires unattended weeks later.
- **Operators replace the wildcard**: `Contains | StartsWith | EndsWith | Exact`,
  plus a `negate` flag giving the "not" variant of each. Always
  case-insensitive; bank descriptions are inconsistently cased and a
  case-sensitive rule fails silently.
- **The join operator governs positives only; negations are always ANDed.** A
  rule reads *"match if (any/all of the positives) and none of the negatives"*.
  This diverges from ag-grid, which applies one operator across the whole set —
  and is what makes `(A OR B) AND NOT C` expressible without a nesting UI.
- **No uniqueness constraint.** A constraint is a thing that stops you creating a
  rule; duplicates are cheap to spot in the dry-run.
- **Every write is previewed.** `POST /api/rules/preview` returns the affected
  rows with current → proposed values and the winning rule id, for a single
  rule, an unsaved draft, or the whole set. Apply recomputes from live data
  rather than replaying the plan.
- **`Category.bucket` is dropped**, migrated into explicit Bucket rules.
- **`Transaction.categoryPinned` / `bucketPinned`** are set when a field is
  edited by hand; rules skip pinned fields.
- **Buckets are assigned to income too.** The per-bank `income → bucket: null`
  gate is removed, so a refund nets against its Bucket — which 0001 described
  but never implemented. Scope a rule to expenses with a `Type` condition.

## Consequences

- The migration is behaviour-preserving by construction and was verified as
  such: `position` is seeded from the old resolution order, and every `*` in the
  23 existing patterns was leading or trailing — decorative under
  implicit-contains — so stripping it and mapping to `Contains` changes no
  match. A dry run over all 1805 transactions produces **0 category changes**.
- **Trailing `*` must migrate to `Contains`, not `StartsWith`.** `Deliveroo*`
  reads like "starts with" but has always matched `PAYMENT TO DELIVEROO` too.
- Deleting `Category.bucket` is irreversible without a prod snapshot.
- **The first run was a cliff, now largely defused.** Provenance was never
  recorded, so pins default to `false` and no past hand-edit is protected. The
  first dry run showed 154 bucket changes — all consequences of bucketing income
  — including three rows manually set to `Ignore` and 10 `Salary` rows the
  seeded (wrong) `Salary → Needs` mapping would have claimed.

  A second migration
  (`20260801110000_correct_seeded_bucket_rules`) fixes this in data rather than
  by hand, so every environment lands in the same corrected state:

  - **`Ignore` rows are pinned.** No Category ever mapped to `Ignore`, so no
    seeded rule can assign it — `bucket = 'Ignore'` is therefore *proof* of a
    manual choice, not a heuristic. Guarded by a `NOT EXISTS` so the inference
    has to still hold at run time.
  - **`Salary → Needs` is deleted.** Salary stays Uncategorised and counts as
    real income; the mapping was harmless only while income was hardcoded to
    `null`.
  - **`Uncategorised → Wants` is deleted**, so an unmatched transaction stays
    unclassified instead of inflating Wants. This was the single biggest
    contributor to the 1150/472 skew.
  - **The two dead `monzo` rules are repointed to `hsbc`**, guarded so it only
    runs while no monzo/flex transaction carries that description — it can
    therefore only improve matters.

  That leaves 134 changes, every one an income row gaining a Bucket, which is
  the intended effect of netting refunds. They remain for the user to review in
  the dry run rather than being applied by migration: a migration correcting
  demonstrably-wrong *rules* is a different thing from a migration silently
  reclassifying 134 *transactions*.
- Precedence is now the user's job. A new rule lands at the bottom of its list
  and may be shadowed by a broader rule above it; the single-rule preview reports
  *matched* and *won* separately so this is legible rather than mysterious.
