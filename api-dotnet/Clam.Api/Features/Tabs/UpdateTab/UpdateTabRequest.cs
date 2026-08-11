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

public sealed class UpdateTabValidator : Validator<UpdateTabRequest>
{
    public UpdateTabValidator()
    {
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
            .When(x => x.Amount is not null);
    }
}
