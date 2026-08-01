# Clam Finance Tracker

A personal finance tracker that imports bank transactions, categorises them, and
scores monthly savings against a 50/30/20 budget.

## Language

**Bucket**:
The 50/30/20 classification of a transaction: one of `Needs`, `Wants`, `Savings`,
or `Ignore`. Replaces the former `SavingType` (`Fixed`/`Fun`/`Saving`) and the
`excludeFromSavings` flag. A Bucket can be unset (null = [[Uncategorised]]).
Applies to both income and expense transactions. A Bucket total is a signed net:
within a Bucket, expenses add and income (refunds) subtract. `Savings` and
`Ignore` are excluded from savings maths in both directions; genuine income
(salary) stays Uncategorised and counts as real income.
_Avoid_: SavingType, Fixed, Fun, Saving, excludeFromSavings

**Rule**:
An ordered instruction that assigns one thing to a transaction automatically.
Two kinds: a **Category rule** assigns a Category, a **Bucket rule** assigns a
[[Bucket]]. Every Category rule is resolved before any Bucket rule, so a Bucket
rule can test the Category a Category rule just assigned. Within a kind, rules
are a single ordered list and the first one that matches wins — nothing else
decides precedence.
_Avoid_: auto-categorize rule, pattern, priority, specificity

**Condition**:
One test inside a [[Rule]]: a field (`Description`, `Category`, `Type`), an
operator (`contains`, `starts with`, `ends with`, `equals`), and a value.
Always case-insensitive. A condition may be **negated**, making it an
_exclusion_. A Rule's join operator (`all` / `any`) applies only to its
non-negated conditions; exclusions always apply. So a Rule reads *"match if
(any or all of the positives) and none of the exclusions"* — a Rule with only
exclusions matches nothing and is rejected.
_Avoid_: filter, clause, criterion

**Dry run**:
Evaluating [[Rule]]s without writing anything, to see which transactions would
change and which Rule would claim each one. For a single Rule it reports
*matched* and *won* separately: a Rule can match many transactions and win none,
because a Rule above it claimed them first.
_Avoid_: preview mode, simulate, test run

**Pin**:
A mark on a transaction's Category or Bucket recording that a person chose it.
Set whenever that field is edited by hand. [[Rule]]s never overwrite a pinned
field, so running Rules over existing transactions cannot erase a deliberate
choice. Category and Bucket are pinned independently.
_Avoid_: lock, manual override, reviewed

**Uncategorised**:
A transaction whose [[Bucket]] is null because no Bucket Rule claimed it and no
manual choice has been made. Distinct from `Ignore`, which is a deliberate
"does not count" choice.
