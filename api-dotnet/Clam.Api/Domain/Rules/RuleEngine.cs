namespace Clam.Api.Domain.Rules;

/// The single source of truth for what a rule matches.
///
/// Static and pure: no database, no clock, no injected services. That is what
/// makes it testable on its own and what stops a second, subtly different
/// matcher growing inside a slice.
public static class RuleEngine
{
    /// Matching is always case-insensitive — bank descriptions are
    /// inconsistently cased, and `TFL TRAVEL` and `Tfl Travel` are the same
    /// merchant.
    private static bool Compare(string haystack, RuleOperator op, string needle)
    {
        var n = needle.Trim();
        return op switch
        {
            RuleOperator.Contains => haystack.Contains(n, StringComparison.OrdinalIgnoreCase),
            RuleOperator.StartsWith => haystack.StartsWith(n, StringComparison.OrdinalIgnoreCase),
            RuleOperator.EndsWith => haystack.EndsWith(n, StringComparison.OrdinalIgnoreCase),
            RuleOperator.Exact => string.Equals(haystack, n, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static string? FieldValue(in MatchableTransaction tx, RuleField field) => field switch
    {
        RuleField.Description => tx.Description,
        RuleField.Category => tx.CategoryName,
        RuleField.Type => tx.Type.ToString(),
        _ => null,
    };

    public static bool MatchesCondition(in MatchableTransaction tx, RuleCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var value = FieldValue(tx, condition.Field);

        // An unresolved field cannot satisfy a positive condition, and cannot
        // violate a negative one — "not contains X" is vacuously true when there
        // is nothing to test.
        if (value is null) return condition.Negate;

        var hit = Compare(value, condition.Operator, condition.Value);
        return condition.Negate ? !hit : hit;
    }

    /// <see cref="Rule.JoinOperator"/> applies to the positive conditions only.
    /// Negated conditions are always ANDed as exclusions, so a rule reads:
    ///
    ///     (any | all of the positives)  AND  none of the negatives
    ///
    /// A rule fires unattended weeks after it was written, and "include this,
    /// except that" is what a rule with exceptions actually means. It also makes
    /// `(A OR B) AND NOT C` expressible without a nesting UI.
    public static bool MatchesRule(in MatchableTransaction tx, Rule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.Bank is not null && !string.Equals(rule.Bank, tx.Bank, StringComparison.Ordinal))
            return false;

        var anyPositive = false;
        var allPositives = true;
        var somePositive = false;

        foreach (var condition in rule.Conditions)
        {
            if (condition.Negate)
            {
                if (!MatchesCondition(tx, condition)) return false;
                continue;
            }

            anyPositive = true;
            if (MatchesCondition(tx, condition)) somePositive = true;
            else allPositives = false;
        }

        // A rule of nothing but exclusions would match almost everything, so it
        // matches nothing instead. Validation rejects it at write time too; this
        // is the second line of defence for rules that predate that check.
        if (!anyPositive) return false;

        return rule.JoinOperator == RuleJoin.OR ? somePositive : allPositives;
    }

    /// First match wins, in <see cref="Rule.Position"/> order. There is no
    /// specificity heuristic: with N conditions and negations, "which rule is
    /// more specific" has no answer a user could predict, so precedence is
    /// explicit and user-owned instead.
    public static Rule? ResolveRule(in MatchableTransaction tx, IReadOnlyList<Rule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        Rule? winner = null;
        foreach (var rule in rules)
        {
            if (!MatchesRule(tx, rule)) continue;
            if (winner is null || rule.Position < winner.Position) winner = rule;
        }
        return winner;
    }

    /// `monzo:tx_123` → `monzo`. Null for rows with no external id, and for an
    /// id carrying no namespace at all.
    public static string? BankOf(string? externalId)
    {
        if (string.IsNullOrEmpty(externalId)) return null;
        var idx = externalId.IndexOf(':', StringComparison.Ordinal);
        return idx <= 0 ? null : externalId[..idx];
    }
}
