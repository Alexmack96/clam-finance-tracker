using Clam.Api.Domain;
using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Investments.CreateInvestmentAccount;

public sealed class CreateInvestmentAccountRequest
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public Owner? Owner { get; set; }
    public double? Rate { get; set; }

    /// Absent means "put it at the bottom" — the command resolves it.
    public int? SortOrder { get; set; }
}

public sealed class CreateInvestmentAccountValidator : Validator<CreateInvestmentAccountRequest>
{
    /// Mirrors the CHECK constraint on the column. Validated here as well so a
    /// typo comes back as a 400 naming the field, not a 500 from the database.
    internal static readonly string[] Categories =
        ["pension", "crypto", "equity", "cash", "commodity", "debt"];

    /// The width of `InvestmentAccounts.name` in db/schema.sql. `Category` needs
    /// no length rule — the membership check below is narrower than the column.
    internal const int MaxNameLength = 200;

    public CreateInvestmentAccountValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("An account needs a name")
            .MaximumLength(MaxNameLength).WithMessage("Name is too long");
        RuleFor(x => x.Category).Must(Categories.Contains).WithMessage("Unknown investment category");
    }
}
