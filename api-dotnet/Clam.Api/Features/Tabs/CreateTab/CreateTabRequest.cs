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
    public CreateTabValidator()
    {
        RuleFor(x => x.Person).NotEmpty().WithMessage("Person is required");
        RuleFor(x => x.Description).NotEmpty().WithMessage("Description is required");

        // Direction carries the sign, so the amount is always a magnitude.
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Amount must be positive");
    }
}
