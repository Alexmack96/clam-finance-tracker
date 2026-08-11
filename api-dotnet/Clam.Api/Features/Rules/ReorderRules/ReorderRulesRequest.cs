using Clam.Api.Domain;
using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Rules.ReorderRules;

/// Precedence is data, not a heuristic — this request is the only thing that
/// decides which of two matching rules wins.
public sealed class ReorderRulesRequest
{
    public RuleKind Kind { get; set; }
    public List<string> Ids { get; set; } = [];
}

public sealed class ReorderRulesValidator : Validator<ReorderRulesRequest>
{
    public ReorderRulesValidator()
    {
        RuleFor(x => x.Ids).NotEmpty();
        RuleForEach(x => x.Ids).NotEmpty();

        // The command checks the list against the rules that exist, by count and
        // by membership — and `[a, a]` against `{a, b}` passes both. The reorder
        // then writes two positions for `a` and none for `b`, leaving `b` at a
        // stale position: precedence silently changes for a rule the user never
        // touched. Distinctness is a property of the request alone, so it belongs
        // here rather than in the command's existence check.
        RuleFor(x => x.Ids)
            .Must(ids => ids.Distinct(StringComparer.Ordinal).Count() == ids.Count)
            .WithMessage("Reorder must not list the same rule twice");
    }
}
