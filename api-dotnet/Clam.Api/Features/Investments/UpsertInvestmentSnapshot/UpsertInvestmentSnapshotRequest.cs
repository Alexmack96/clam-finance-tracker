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

public sealed class UpsertInvestmentSnapshotValidator : Validator<UpsertInvestmentSnapshotRequest>
{
    public UpsertInvestmentSnapshotValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.Date).NotEqual(default(DateTime)).WithMessage("A snapshot needs a date");
    }
}
