using Clam.Api.Features.Rules.PreviewRules;
using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Rules.ApplyRules;

/// Apply takes the same scopes as preview minus `draft` — you cannot commit a
/// rule that does not exist. Reusing <see cref="PreviewScope"/> and rejecting
/// the third member in the validator keeps the two request bodies identical
/// where they overlap, which is what the client sends.
public sealed class ApplyRulesRequest
{
    public PreviewScope Scope { get; set; }
    public string? RuleId { get; set; }
}

public sealed class ApplyRulesValidator : Validator<ApplyRulesRequest>
{
    public ApplyRulesValidator()
    {
        RuleFor(x => x.Scope)
            .NotEqual(PreviewScope.Draft)
            .WithMessage("A draft rule must be saved before it can be applied");

        RuleFor(x => x.RuleId)
            .NotEmpty().WithMessage("ruleId is required when scope is rule")
            .When(x => x.Scope == PreviewScope.Rule);
    }
}
