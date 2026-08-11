using Clam.Api.Domain;
using Clam.Api.Features.Tabs.CreateTab;
using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Tabs.UpdateTab;

public sealed class UpdateTabRequest
{
    public string Id { get; set; } = "";
    public string? Person { get; set; }
    public string? Description { get; set; }
    public decimal? Amount { get; set; }
    public TabDirection? Direction { get; set; }
    public TabStatus? Status { get; set; }

    /// Rarely sent: settling a tab normally just sets `status`, and the command
    /// stamps the time. This exists for backdating one.
    public DateTime? SettledAt { get; set; }
}

/// Takes a <see cref="TimeProvider"/> for the same reason the snapshot validator
/// does: "not in the future" is only testable against an injected clock.
public sealed class UpdateTabValidator : Validator<UpdateTabRequest>
{
    public UpdateTabValidator(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        RuleFor(x => x.Person!)
            .NotEmpty()
            .MaximumLength(CreateTabValidator.MaxPersonLength).WithMessage("Person is too long")
            .When(x => x.Person is not null);

        RuleFor(x => x.Description!)
            .NotEmpty()
            .MaximumLength(CreateTabValidator.MaxDescriptionLength).WithMessage("Description is too long")
            .When(x => x.Description is not null);
        // `Must` on the nullable rather than `GreaterThan` on `.Value`: the
        // latter names the failure `amount.Value`, which is not a field the
        // client sent and so cannot be mapped back to an input.
        RuleFor(x => x.Amount)
            .Must(amount => amount > 0).WithMessage("Amount must be positive")
            .Must(amount => amount <= CreateTabValidator.MaxAmount)
            .WithMessage($"Amount must be {CreateTabValidator.MaxAmount:N0} or less")
            .Must(amount => CreateTabValidator.IsWithinScale(amount!.Value))
            .WithMessage($"Amount cannot have more than {CreateTabValidator.AmountScale} decimal places")
            .When(x => x.Amount is not null);

        // Backdating a settlement is the point of this field; forward-dating one
        // records a tab as settled on a day that has not happened.
        RuleFor(x => x.SettledAt)
            .Must(settledAt => settledAt <= clock.GetUtcNow().UtcDateTime)
            .WithMessage("A tab cannot be settled in the future")
            .When(x => x.SettledAt is not null);

        // The command gives an explicit `settledAt` priority over the status, so
        // sending both would store a settlement date on a tab the same request
        // just reopened.
        RuleFor(x => x.SettledAt)
            .Null().WithMessage("An open tab cannot have a settled date")
            .When(x => x.Status == TabStatus.Open);
    }
}
