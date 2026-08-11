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
    }
}
