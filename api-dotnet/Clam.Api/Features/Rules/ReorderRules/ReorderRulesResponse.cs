using Clam.Api.Domain.Rules;

namespace Clam.Api.Features.Rules.ReorderRules;

/// The reordered rules of that kind, in their new order — a bare array, as the
/// Express route returns.
public sealed class ReorderRulesResponse : List<Rule>
{
    public ReorderRulesResponse(IEnumerable<Rule> rules) : base(rules) { }
}
