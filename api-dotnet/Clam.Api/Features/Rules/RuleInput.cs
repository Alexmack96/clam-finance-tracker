using Clam.Api.Domain;
using FluentValidation;

namespace Clam.Api.Features.Rules;

/// The body of a rule as the client writes it. Feature-shared (tier 2) on the
/// rule of three: create, update and the draft branch of preview all take this
/// exact shape, and the third one is what settled it.
///
/// Not the same type as <see cref="Domain.Rules.Rule"/>: a rule you send has no
/// id, no position and no createdAt, and its conditions have no ids either.
/// Reusing the domain type would mean four properties the caller must not set.
public class RuleInput
{
    public RuleKind Kind { get; set; }
    public RuleJoin JoinOperator { get; set; } = RuleJoin.AND;
    public string? Bank { get; set; }
    public string? CategoryId { get; set; }
    public Bucket? Bucket { get; set; }
    public List<RuleConditionInput> Conditions { get; set; } = [];
}

public sealed class RuleConditionInput
{
    public RuleField Field { get; set; } = RuleField.Description;
    public RuleOperator Operator { get; set; } = RuleOperator.Contains;
    public string Value { get; set; } = "";
    public bool Negate { get; set; }
}

/// The .NET half of `createRuleSchema`. Registered as a child validator by the
/// slices that accept a rule body, rather than discovered on its own — it
/// validates a property, not a request.
public sealed class RuleInputValidator : AbstractValidator<RuleInput>
{
    /// The banks a rule may scope itself to. Free text in the database, because
    /// the value is matched against an `externalId` namespace, but a typo here
    /// produces a rule that silently matches nothing — so writes are checked.
    private static readonly string[] KnownBanks =
        ["monzo", "flex", "amex", "barclays", "santander", "hsbc", "sofi", "chase"];

    /// Every condition is a row in [RuleConditions] and is re-evaluated against
    /// every transaction on every import, dry-run and apply. A rule this wide is
    /// a mistake rather than an intent — the ceiling is here so the cost of one
    /// is bounded by something other than how much JSON fits in a request.
    internal const int MaxConditions = 20;

    public RuleInputValidator()
    {
        RuleFor(r => r.Conditions)
            .NotEmpty().WithMessage("At least one condition is required")
            .Must(conditions => conditions.Count <= MaxConditions)
            .WithMessage($"A rule cannot have more than {MaxConditions} conditions");

        RuleForEach(r => r.Conditions).ChildRules(condition =>
        {
            condition.RuleFor(c => c.Value)
                .NotEmpty().WithMessage("Value is required")
                .MaximumLength(200).WithMessage("Value is too long");
        });

        RuleFor(r => r.Bank!)
            .Must(bank => KnownBanks.Contains(bank, StringComparer.Ordinal))
            .WithMessage("Unknown bank")
            .When(r => r.Bank is not null);

        // A rule with only negated conditions would match nearly every
        // transaction, so it is rejected rather than left as a foot-gun that
        // fires unattended during an import.
        RuleFor(r => r.Conditions)
            .Must(conditions => conditions.Exists(c => !c.Negate))
            .WithMessage("A rule needs at least one non-negated condition")
            .When(r => r.Conditions.Count > 0);

        // The output has to match the kind — a Category rule with no category
        // assigns nothing.
        RuleFor(r => r.CategoryId)
            .NotEmpty().WithMessage("A category rule must set a category")
            .When(r => r.Kind == RuleKind.Category);

        // [Rules].[categoryId] is an id column and this value is INSERTed into
        // it, so an over-long one is a truncation 500 rather than the "no such
        // category" the caller deserves. Checked on both kinds: a Bucket rule may
        // still carry a categoryId, and it is stored just the same.
        RuleFor(r => r.CategoryId!)
            .MaximumLength(Ids.MaxLength).WithMessage("categoryId is not an id")
            .When(r => r.CategoryId is not null);

        RuleFor(r => r.Bucket)
            .NotNull().WithMessage("A bucket rule must set a bucket")
            .When(r => r.Kind == RuleKind.Bucket);
    }
}
