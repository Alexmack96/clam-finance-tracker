using Ardalis.Result;
using Clam.Api.Domain.Rules;
using Clam.Api.Features.Rules.PreviewRules;
using Clam.Api.Infrastructure.Data;

namespace Clam.Api.Features.Rules.ApplyRules;

public sealed class ApplyRulesCommand(IDbConnectionFactory factory)
{
    public async Task<Result<ApplyRulesResponse>> ExecuteAsync(
        ApplyRulesRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);

        var rules = await RuleStore.LoadRulesAsync(connection, ct);
        var categoryNameById = await RuleStore.LoadCategoryNamesAsync(connection, ct);
        var transactions = await RulePlanStore.LoadPlanTransactionsAsync(connection, ct);

        var focusId = request.Scope == PreviewScope.Rule ? request.RuleId : null;
        if (focusId is not null && !rules.Exists(r => r.Id == focusId))
            return Result<ApplyRulesResponse>.NotFound("Rule not found");

        // Recomputed from live data rather than replaying the previewed plan, so
        // an import that landed between preview and confirm is included rather
        // than silently skipped. The counts come back so the client can report
        // what happened rather than what was promised.
        var plan = RulePlanner.BuildPlan(transactions, rules, categoryNameById, focusId);
        var (categoryChanges, bucketChanges) = await RulePlanStore.ApplyPlanAsync(connection, plan, ct);

        return Result<ApplyRulesResponse>.Success(new ApplyRulesResponse
        {
            CategoryChanges = categoryChanges,
            BucketChanges = bucketChanges,
            Affected = plan.Rows.Count,
            PinnedSkipped = plan.PinnedSkipped,
        });
    }
}
