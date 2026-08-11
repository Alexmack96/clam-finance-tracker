using Clam.Api.Domain;
using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Recurring.SetRecurringVerdict;

public sealed class SetRecurringVerdictRequest
{
    public Owner Owner { get; set; }
    public string Description { get; set; } = "";

    /// null clears the verdict, returning the series to Proposed.
    public RecurringStatus? Status { get; set; }
}

public sealed class SetRecurringVerdictValidator : Validator<SetRecurringVerdictRequest>
{
    public SetRecurringVerdictValidator()
    {
        RuleFor(x => x.Owner)
            .Must(RecurringOwner.IsSupported)
            .WithMessage("Recurring series belong to a person, not to Joint");

        RuleFor(x => x.Description).NotEmpty().MaximumLength(400);
    }
}
