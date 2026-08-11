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

    /// A tab is one person owing another for a shared cost, so a million is a
    /// ceiling nobody reaches by intent. It also sits well inside the column:
    /// `DECIMAL(18, 2)` holds sixteen integer digits, and a `decimal` past that
    /// binds cleanly and then dies as an arithmetic overflow on the INSERT — a
    /// 500 the caller cannot tell from a real failure. (`1e40` never gets that
    /// far; it overflows `decimal` itself and the binder rejects it.)
    internal const decimal MaxAmount = 1_000_000m;

    /// The column keeps two, so a third is not stored — it is silently rounded
    /// away, and 10.999 comes back as 11.00 having been accepted as written.
    internal const int AmountScale = 2;

    internal static bool IsWithinScale(decimal amount) => decimal.Round(amount, AmountScale) == amount;

    public CreateTabValidator()
    {
        RuleFor(x => x.Person)
            .NotEmpty().WithMessage("Person is required")
            .MaximumLength(MaxPersonLength).WithMessage("Person is too long");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required")
            .MaximumLength(MaxDescriptionLength).WithMessage("Description is too long");

        // Direction carries the sign, so the amount is always a magnitude.
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount must be positive")
            .LessThanOrEqualTo(MaxAmount).WithMessage($"Amount must be {MaxAmount:N0} or less")
            .Must(IsWithinScale).WithMessage($"Amount cannot have more than {AmountScale} decimal places");
    }
}
