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

    /// `rate` is an interest or growth rate in percent, shown as typed. The
    /// column is FLOAT, so without a bound a fat-fingered `450` — or a `1e308`
    /// from a client bug — is stored and then projected forward. Negative is
    /// allowed: a `debt` account's rate is a cost, and negative deposit rates
    /// have happened.
    internal const double MinRate = -100;
    internal const double MaxRate = 100;

    public CreateInvestmentAccountValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("An account needs a name")
            .MaximumLength(MaxNameLength).WithMessage("Name is too long");
        RuleFor(x => x.Category).Must(Categories.Contains).WithMessage("Unknown investment category");

        RuleFor(x => x.Rate)
            .InclusiveBetween(MinRate, MaxRate)
            .WithMessage($"Rate must be between {MinRate:N0} and {MaxRate:N0} percent")
            .When(x => x.Rate is not null);
    }
}
