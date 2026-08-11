using Clam.Api.Domain;
using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Investments.UpsertInvestmentSnapshot;

/// A PUT, because (accountId, date) is the identity: re-entering a value for a
/// day you have already recorded corrects it rather than duplicating it.
public sealed class UpsertInvestmentSnapshotRequest
{
    public string AccountId { get; set; } = "";
    public DateTime Date { get; set; }
    public double Value { get; set; }
}

/// Takes a <see cref="TimeProvider"/> rather than reading DateTime.UtcNow —
/// FastEndpoints resolves validators from DI, so "not in the future" is a rule a
/// test can put a date either side of.
public sealed class UpsertInvestmentSnapshotValidator : Validator<UpsertInvestmentSnapshotRequest>
{
    /// A snapshot is a reading taken on a day, so the portfolio has no history
    /// before the app has users. The floor is here to catch a two-digit year or a
    /// unix epoch that survived a client-side date parse, not to police history.
    internal static readonly DateTime Earliest = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// Sterling, and an order of magnitude past anything this app is for. The
    /// column is FLOAT, so without a ceiling `1e308` is stored happily and every
    /// chart it appears in is a flat line with one spike.
    internal const double MaxValue = 100_000_000;

    public UpsertInvestmentSnapshotValidator(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        RuleFor(x => x.AccountId)
            .NotEmpty().WithMessage("A snapshot needs an account")
            .MaximumLength(Ids.MaxLength).WithMessage("accountId is not an id");

        RuleFor(x => x.Date).NotEqual(default(DateTime)).WithMessage("A snapshot needs a date");

        // A future snapshot is not merely odd — it becomes the newest row, so it
        // wins every "latest value" read and is reported as the current holding.
        RuleFor(x => x.Date)
            .LessThanOrEqualTo(_ => clock.GetUtcNow().UtcDateTime)
            .WithMessage("A snapshot cannot be dated in the future")
            .GreaterThanOrEqualTo(Earliest)
            .WithMessage($"A snapshot cannot be dated before {Earliest:yyyy-MM-dd}")
            .When(x => x.Date != default);

        // Negative is allowed on purpose: a `debt` account is a negative holding,
        // and netting it off is the whole reason that category exists.
        RuleFor(x => x.Value)
            .InclusiveBetween(-MaxValue, MaxValue)
            .WithMessage($"Value must be between {-MaxValue:N0} and {MaxValue:N0}");
    }
}
