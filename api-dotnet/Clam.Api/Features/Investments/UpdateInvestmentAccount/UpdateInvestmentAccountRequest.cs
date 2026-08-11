using Clam.Api.Features.Investments.CreateInvestmentAccount;
using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Investments.UpdateInvestmentAccount;

/// Owner is deliberately absent: moving an account between people would move
/// its whole snapshot history with it and silently rewrite both NAV curves.
public sealed class UpdateInvestmentAccountRequest
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Category { get; set; }
    public double? Rate { get; set; }
    public int? SortOrder { get; set; }
}

public sealed class UpdateInvestmentAccountValidator : Validator<UpdateInvestmentAccountRequest>
{
    public UpdateInvestmentAccountValidator()
    {
        RuleFor(x => x.Name!)
            .NotEmpty().WithMessage("An account needs a name")
            .MaximumLength(CreateInvestmentAccountValidator.MaxNameLength).WithMessage("Name is too long")
            .When(x => x.Name is not null);

        RuleFor(x => x.Category!)
            .Must(CreateInvestmentAccountValidator.Categories.Contains)
            .WithMessage("Unknown investment category")
            .When(x => x.Category is not null);
    }
}
