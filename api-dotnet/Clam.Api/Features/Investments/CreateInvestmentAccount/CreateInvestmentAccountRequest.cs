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

    public CreateInvestmentAccountValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Category).Must(Categories.Contains).WithMessage("Unknown investment category");
    }
}
