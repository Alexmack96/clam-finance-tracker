using Ardalis.Result;
using Clam.Api.Domain.Rules;
using Clam.Api.Infrastructure.Data;

namespace Clam.Api.Features.Rules.PreviewRules;

public sealed class PreviewRulesQuery(IDbConnectionFactory factory, TimeProvider clock)
{
    /// The id a draft is given for the duration of one preview. It never
    /// reaches the database; it exists so the planner can be asked "which rows
    /// does *this* rule win" about a rule that has no id yet.
    private const string DraftId = "__draft__";

    public async Task<Result<PreviewRulesResponse>> ExecuteAsync(
        PreviewRulesRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);

        var saved = await RuleStore.LoadRulesAsync(connection, ct);
        var categoryNameById = await RuleStore.LoadCategoryNamesAsync(connection, ct);
        var transactions = await RulePlanStore.LoadPlanTransactionsAsync(connection, ct);

        if (request.Scope == PreviewScope.All)
        {
            var wholePlan = RulePlanner.BuildPlan(transactions, saved, categoryNameById);
            return Result<PreviewRulesResponse>.Success(ToPreview(wholePlan, categoryNameById));
        }

        List<Rule> rules;
        string focusId;

        if (request.Scope == PreviewScope.Rule)
        {
            focusId = request.RuleId!;
            if (!saved.Exists(r => r.Id == focusId))
                return Result<PreviewRulesResponse>.NotFound("Rule not found");
            rules = saved;
        }
        else
        {
            var replacing = request.RuleId is null
                ? null
                : saved.Find(r => r.Id == request.RuleId);

            focusId = DraftId;
            rules = BuildDraftRuleSet(saved, request.Rule!, replacing, focusId, clock.GetUtcNow().UtcDateTime);
        }

        var focus = rules.Find(r => r.Id == focusId);
        if (focus is null) return Result<PreviewRulesResponse>.NotFound("Rule not found");

        // `Matched` counts everything the conditions accept; `Won` counts the
        // subset no higher rule claimed first. Without both, a rule reporting
        // zero changes is indistinguishable from one that matches nothing.
        var plan = RulePlanner.BuildPlan(transactions, rules, categoryNameById, focusId);
        var matched = RulePlanner.CountMatches(transactions, focus, categoryNameById);
        var won = RulePlanner.CountWins(transactions, rules, categoryNameById, focusId);

        return Result<PreviewRulesResponse>.Success(ToPreview(plan, categoryNameById, matched, won));
    }

    /// The draft slots into the position it would really occupy — the one it is
    /// replacing when editing, or the bottom of its kind when new. Preview a
    /// draft at the wrong position and the "won" count is fiction.
    private static List<Rule> BuildDraftRuleSet(
        List<Rule> saved,
        RuleInput draft,
        Rule? replacing,
        string draftId,
        DateTime now)
    {
        var lastPosition = saved
            .Where(r => r.Kind == draft.Kind)
            .Aggregate(-1, (max, r) => Math.Max(max, r.Position));

        var draftRule = new Rule
        {
            Id = draftId,
            Kind = draft.Kind,
            Position = replacing?.Position ?? lastPosition + 1,
            JoinOperator = draft.JoinOperator,
            Bank = draft.Bank,
            CategoryId = draft.Kind == Domain.RuleKind.Category ? draft.CategoryId : null,
            Bucket = draft.Kind == Domain.RuleKind.Bucket ? draft.Bucket : null,
            CreatedAt = now,
            Conditions = [.. draft.Conditions.Select((c, i) => new RuleCondition
            {
                Id = $"draft-{i}",
                Field = c.Field,
                Operator = c.Operator,
                Value = c.Value,
                Negate = c.Negate,
                Position = i,
            })],
        };

        var rules = saved.Where(r => replacing is null || r.Id != replacing.Id).ToList();
        rules.Add(draftRule);
        return rules;
    }

    private static PreviewRulesResponse ToPreview(
        Plan plan,
        IReadOnlyDictionary<string, string> categoryNameById,
        int? matched = null,
        int? won = null) => new()
        {
            Rows = [.. plan.Rows.Select(r => new RulePreviewRow
            {
                Id = r.Transaction.Id,
                Date = r.Transaction.Date,
                Description = r.Transaction.Description,
                Amount = (double)r.Transaction.Amount,
                Type = r.Transaction.Type,
                Bank = RuleEngine.BankOf(r.Transaction.ExternalId),
                CurrentCategory = categoryNameById.GetValueOrDefault(r.Transaction.CategoryId, "—"),
                ProposedCategory = r.NextCategoryId is null
                    ? null
                    : categoryNameById.GetValueOrDefault(r.NextCategoryId, "—"),
                CurrentBucket = r.Transaction.Bucket,
                ProposedBucket = r.NextBucket,
                CategoryRuleId = r.CategoryRuleId,
                BucketRuleId = r.BucketRuleId,
            })],
            Scanned = plan.Scanned,
            CategoryChanges = plan.Rows.Count(r => r.NextCategoryId is not null),
            BucketChanges = plan.Rows.Count(r => r.NextBucket is not null),
            PinnedSkipped = plan.PinnedSkipped,
            Matched = matched,
            Won = won,
        };
}
