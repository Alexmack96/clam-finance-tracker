using Clam.Api.Domain;
using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Tabs.CreateTab;

public sealed class CreateTabRequest
{
    public string Person { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public TabDirection Direction { get; set; }
}

public sealed class CreateTabValidator : Validator<CreateTabRequest>
{
    /// The widths of `Tabs.person` and `Tabs.description` in db/schema.sql.
    /// Without these the insert reaches SQL Server and comes back a 500.
    internal const int MaxPersonLength = 200;
    internal const int MaxDescriptionLength = 500;

    public CreateTabValidator()
    {
        RuleFor(x => x.Person)
            .NotEmpty().WithMessage("Person is required")
            .MaximumLength(MaxPersonLength).WithMessage("Person is too long");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required")
            .MaximumLength(MaxDescriptionLength).WithMessage("Description is too long");

        // Direction carries the sign, so the amount is always a magnitude.
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Amount must be positive");
    }
}
