using Clam.Api.Domain;
using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Recurring.UpdateRecurringNote;

/// Free-text note on a series, for "cancelled 3 Aug, refund pending" style detail.
public sealed class UpdateRecurringNoteRequest
{
    public Owner Owner { get; set; }
    public string Description { get; set; } = "";
    public string? Note { get; set; }
}

public sealed class UpdateRecurringNoteValidator : Validator<UpdateRecurringNoteRequest>
{
    public UpdateRecurringNoteValidator()
    {
        RuleFor(x => x.Owner)
            .Must(RecurringOwner.IsSupported)
            .WithMessage("Recurring series belong to a person, not to Joint");

        RuleFor(x => x.Description).NotEmpty().MaximumLength(400);
        RuleFor(x => x.Note!).MaximumLength(500).When(x => x.Note is not null);
    }
}
