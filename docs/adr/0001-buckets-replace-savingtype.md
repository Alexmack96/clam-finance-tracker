# 1. Buckets replace SavingType for 50/30/20 classification

Date: 2026-07-22

## Status

Accepted

## Context

Transactions were classified for the 50/30/20 savings model via a `SavingType`
enum (`Fixed`/`Fun`/`Saving`) on `Category`, an optional per-transaction
`savingType` override where `null` meant "inherit from the category live", and a
separate `excludeFromSavings` boolean. This had three problems:

- **Live inheritance was confusing.** A transaction's effective classification
  depended on reading its category at query time; changing a category silently
  moved historical transactions.
- **Two overlapping axes.** `savingType` and `excludeFromSavings` both fed the
  savings maths with subtly different rules.
- **No home for refunds.** Money-in that offsets a spend (a returned purchase)
  had nowhere to net against.

## Decision

Replace all three with a single nullable **Bucket** = `Needs | Wants | Savings |
Ignore`, on both `Category` and `Transaction`.

- **Transaction Bucket is the single source of truth** for all reads/maths. The
  Category Bucket is only a _mapping_ that stamps a value onto a transaction at
  import time — never consulted at read time. This kills live inheritance.
- **Category mapping auto-stamps Expenses only.** Income imports as `null`, so
  genuine salary stays "real income"; refunds/transfers are tagged by hand.
- **Setting/changing a Category mapping backfills only unset (null) transactions**
  in that category, preserving manual overrides.
- **Buckets net.** Within a Bucket, expenses add and income subtracts, so a
  refund cancels its spend. `Savings` and `Ignore` are excluded in both
  directions. `null` expense counts as spend; `null` income counts as real
  income.
- **`null` is birth-only.** The UI never offers "unset" as a choice; once a
  Bucket is picked you flip among the four but cannot return to `null`.
- The `SavingType` enum, `Category.savingType`, `Transaction.savingType`, and
  `Transaction.excludeFromSavings` are dropped in one migration after backfill
  (`Fixed→Needs`, `Fun→Wants`, `Saving→Savings`, `excludeFromSavings→Ignore`).

## Consequences

- Historical transactions are frozen at their migrated Bucket; re-mapping a
  category later does not move them (fill-nulls-only backfill).
- The Analytics "Fun Budget" gauge becomes the "Wants Budget" and switches from
  owner-only to `selected owner + ½ Joint`, so it reads higher than before.
- Column drops are irreversible without restoring a prod snapshot.
- Refunds now model correctly for the first time, at the cost of remembering to
  bucket the occasional income row.
