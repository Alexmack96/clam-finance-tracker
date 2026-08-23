namespace Clam.Api.Features.Import.ImportChase;

/// One entry off a Chase credit-card statement, in the statement's own terms.
///
/// A card statement prints one amount column, so direction is carried by
/// <see cref="IsCredit"/> rather than by two money fields the way a current
/// account does. Chase signs the figure itself — credits print negative — and
/// that sign is where the flag comes from, not the section heading: a refund
/// prints under PURCHASE with a minus, and reading the heading alone would book
/// it as spending.
public sealed record ChaseRow
{
    /// ISO. The statement prints "02/08" with no year, so the year is inferred
    /// from the statement's own closing date.
    public required string Date { get; init; }

    public required string Description { get; init; }

    /// USD, unsigned, with the statement's comma grouping stripped. The sign
    /// lives in <see cref="IsCredit"/> — the process step converts this figure
    /// to sterling and a negative would come back as a negative expense.
    public required string Amount { get; init; }

    public required bool IsCredit { get; init; }

    /// "Feb 2026", off the statement's closing date.
    public required string StatementDate { get; init; }
}
