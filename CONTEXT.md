# Clam Finance Tracker

A personal finance tracker that imports bank transactions, categorises them, and
scores monthly savings against a 50/30/20 budget.

## Language

**Bucket**:
The 50/30/20 classification of a transaction: one of `Needs`, `Wants`, `Savings`,
or `Ignore`. Replaces the former `SavingType` (`Fixed`/`Fun`/`Saving`) and the
`excludeFromSavings` flag. A Bucket can be unset (null = uncategorised). Applies to
both income and expense transactions. A Bucket total is a signed net: within a
Bucket, expenses add and income (refunds) subtract. `Savings` and `Ignore` are
excluded from savings maths in both directions; genuine income (salary) stays
Uncategorised and counts as real income.
_Avoid_: SavingType, Fixed, Fun, Saving, excludeFromSavings

**Category Bucket mapping**:
A per-Category default Bucket (e.g. Rent→Needs, Investments→Savings). Used to
populate a transaction's Bucket at import time so most transactions don't need
manual classification. Not a live inheritance — the value is copied onto the
transaction, which can then be overridden independently.
_Avoid_: inherit, default savingType

**Uncategorised**:
A transaction whose Bucket is null because its Category has no mapping and no
manual choice has been made. Distinct from `Ignore`, which is a deliberate
"does not count" choice.
