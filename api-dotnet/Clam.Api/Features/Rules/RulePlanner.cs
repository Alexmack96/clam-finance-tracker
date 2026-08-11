using Clam.Api.Domain;
using Clam.Api.Domain.Rules;

namespace Clam.Api.Features.Rules;

/// The fields a plan needs off a transaction. Deliberately not the full row: a
/// plan is built over every transaction in the database, and the ones it does
/// not change are the overwhelming majority.
public sealed class PlanTransaction
{
    public string Id { get; set; } = "";
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public string? ExternalId { get; set; }
    public string CategoryId { get; set; } = "";
    public Bucket? Bucket { get; set; }
    public bool CategoryPinned { get; set; }
    public bool BucketPinned { get; set; }
}

public sealed class PlanRow
{
    public required PlanTransaction Transaction { get; init; }
    public string? NextCategoryId { get; init; }
    public Bucket? NextBucket { get; init; }
    public string? CategoryRuleId { get; init; }
    public string? BucketRuleId { get; init; }
}

public sealed class Plan
{
    public required IReadOnlyList<PlanRow> Rows { get; init; }
    public int Scanned { get; init; }
    public int PinnedSkipped { get; init; }
}

/// Feature-shared (tier 2): the preview and the apply slices are the same
/// computation, run twice — once to show and once to commit. Recomputing rather
/// than replaying a stored preview is what makes an import that lands between
/// the two get included instead of silently skipped.
public static class RulePlanner
{
    /// Runs the two-pass pipeline over every transaction and returns only the
    /// rows whose category or bucket would actually change.
    ///
    /// <paramref name="focusRuleId"/> narrows the result to rows that rule
    /// *wins* — not merely matches. A rule lower down the list can match plenty
    /// and win nothing, which is exactly what a single-rule preview must show.
    public static Plan BuildPlan(
        IReadOnlyList<PlanTransaction> transactions,
        IReadOnlyList<Rule> rules,
        IReadOnlyDictionary<string, string> categoryNameById,
        string? focusRuleId = null)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(categoryNameById);

        var categoryRules = rules.Where(r => r.Kind == RuleKind.Category).ToList();
        var bucketRules = rules.Where(r => r.Kind == RuleKind.Bucket).ToList();

        var rows = new List<PlanRow>();
        var pinnedSkipped = 0;

        foreach (var tx in transactions)
        {
            var basis = ToMatchable(tx, categoryNameById);

            var categoryWinner = RuleEngine.ResolveRule(basis, categoryRules);
            var proposedCategoryId = categoryWinner?.CategoryId;

            // A pinned category is never replaced, so the bucket pass must see
            // the category that will actually be in place — not the one a rule
            // wanted.
            var effectiveCategoryId = tx.CategoryPinned
                ? tx.CategoryId
                : proposedCategoryId ?? tx.CategoryId;

            var bucketBasis = basis with { CategoryName = categoryNameById.GetValueOrDefault(effectiveCategoryId) };
            var bucketWinner = RuleEngine.ResolveRule(bucketBasis, bucketRules);
            var proposedBucket = bucketWinner?.Bucket;

            var categoryWouldChange = proposedCategoryId is not null
                && !string.Equals(proposedCategoryId, tx.CategoryId, StringComparison.Ordinal);
            var bucketWouldChange = proposedBucket is not null && proposedBucket != tx.Bucket;

            var categoryChanges = !tx.CategoryPinned && categoryWouldChange;
            var bucketChanges = !tx.BucketPinned && bucketWouldChange;

            if (tx.CategoryPinned && categoryWouldChange) pinnedSkipped++;
            else if (tx.BucketPinned && bucketWouldChange) pinnedSkipped++;

            if (!categoryChanges && !bucketChanges) continue;

            if (focusRuleId is not null)
            {
                var wins =
                    (categoryChanges && categoryWinner?.Id == focusRuleId)
                    || (bucketChanges && bucketWinner?.Id == focusRuleId);
                if (!wins) continue;
            }

            rows.Add(new PlanRow
            {
                Transaction = tx,
                NextCategoryId = categoryChanges ? proposedCategoryId : null,
                NextBucket = bucketChanges ? proposedBucket : null,
                CategoryRuleId = categoryChanges ? categoryWinner?.Id : null,
                BucketRuleId = bucketChanges ? bucketWinner?.Id : null,
            });
        }

        return new Plan { Rows = rows, Scanned = transactions.Count, PinnedSkipped = pinnedSkipped };
    }

    /// How many transactions a single rule's conditions accept, ignoring
    /// precedence.
    public static int CountMatches(
        IReadOnlyList<PlanTransaction> transactions,
        Rule rule,
        IReadOnlyDictionary<string, string> categoryNameById)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        return transactions.Count(tx => RuleEngine.MatchesRule(ToMatchable(tx, categoryNameById), rule));
    }

    /// How many transactions a rule is the *winner* for, whether or not it
    /// changes anything. Paired with <see cref="CountMatches"/> this is what
    /// makes a rule reporting zero changes intelligible: matched 26 / won 3
    /// means higher rules took the rest.
    public static int CountWins(
        IReadOnlyList<PlanTransaction> transactions,
        IReadOnlyList<Rule> rules,
        IReadOnlyDictionary<string, string> categoryNameById,
        string focusRuleId)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(rules);

        var focus = rules.FirstOrDefault(r => r.Id == focusRuleId);
        if (focus is null) return 0;

        var sameKind = rules.Where(r => r.Kind == focus.Kind).ToList();
        var categoryRules = rules.Where(r => r.Kind == RuleKind.Category).ToList();

        var wins = 0;
        foreach (var tx in transactions)
        {
            var basis = ToMatchable(tx, categoryNameById);

            // A Bucket rule is judged against the category that will actually be
            // in place once the Category pass has run — the same context the
            // real run uses.
            if (focus.Kind == RuleKind.Bucket)
            {
                var proposed = RuleEngine.ResolveRule(basis, categoryRules)?.CategoryId;
                var effective = tx.CategoryPinned ? tx.CategoryId : proposed ?? tx.CategoryId;
                basis = basis with { CategoryName = categoryNameById.GetValueOrDefault(effective) };
            }

            if (RuleEngine.ResolveRule(basis, sameKind)?.Id == focusRuleId) wins++;
        }

        return wins;
    }

    private static MatchableTransaction ToMatchable(
        PlanTransaction tx,
        IReadOnlyDictionary<string, string> categoryNameById) =>
        new(tx.Description, tx.Type, categoryNameById.GetValueOrDefault(tx.CategoryId), RuleEngine.BankOf(tx.ExternalId));
}
