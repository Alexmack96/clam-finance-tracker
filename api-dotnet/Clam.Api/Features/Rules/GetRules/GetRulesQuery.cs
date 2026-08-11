using Clam.Api.Domain.Rules;
using Clam.Api.Infrastructure.Data;

namespace Clam.Api.Features.Rules.GetRules;

/// Thin on purpose: listing rules is exactly "load the rules", which
/// <see cref="RuleStore"/> already owns because the import pipeline needs the
/// same thing. A second query here would be a second definition of the rule set.
public sealed class GetRulesQuery(IDbConnectionFactory factory)
{
    public async Task<IReadOnlyList<Rule>> ExecuteAsync(CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);
        return await RuleStore.LoadRulesAsync(connection, ct);
    }
}
