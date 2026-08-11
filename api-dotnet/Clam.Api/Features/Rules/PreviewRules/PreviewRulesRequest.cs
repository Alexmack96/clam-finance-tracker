using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Rules.PreviewRules;

public enum PreviewScope
{
    /// Every saved rule, over every transaction.
    All,

    /// One saved rule, reported as matched / won / changed.
    Rule,

    /// A rule that has not been saved yet, so you can see what it catches
    /// without first committing it.
    Draft,
}

/// The .NET spelling of the zod discriminated union. One flat type with a
/// discriminator and two optional members, rather than three request types:
/// FastEndpoints binds one body to one DTO, and the validator below is what
/// re-establishes the "these fields go together" guarantee the union gave.
public sealed class PreviewRulesRequest
{
    public PreviewScope Scope { get; set; }

    /// The saved rule to focus on. Required for <see cref="PreviewScope.Rule"/>;
    /// optional for <see cref="PreviewScope.Draft"/>, where it means the draft
    /// slots into that rule's position, replacing it.
    public string? RuleId { get; set; }

    public RuleInput? Rule { get; set; }
}

public sealed class PreviewRulesValidator : Validator<PreviewRulesRequest>
{
    public PreviewRulesValidator()
    {
        RuleFor(x => x.RuleId)
            .NotEmpty().WithMessage("ruleId is required when scope is rule")
            .When(x => x.Scope == PreviewScope.Rule);

        RuleFor(x => x.Rule)
            .NotNull().WithMessage("rule is required when scope is draft")
            .When(x => x.Scope == PreviewScope.Draft);

        RuleFor(x => x.Rule!)
            .SetValidator(new RuleInputValidator())
            .When(x => x.Rule is not null);
    }
}
