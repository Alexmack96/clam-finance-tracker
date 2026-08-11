using Clam.Api.Domain.Rules;

namespace Clam.Api.Features.Rules.GetRules;

/// A bare JSON array, matching `res.json(rules.map(toRule))`. See
/// GetTransactionsResponse for why the collection is the response type rather
/// than a property on it.
public sealed class GetRulesResponse : List<Rule>
{
    public GetRulesResponse(IEnumerable<Rule> rules) : base(rules) { }
}
