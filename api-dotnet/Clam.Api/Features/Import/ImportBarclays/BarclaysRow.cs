namespace Clam.Api.Features.Import.ImportBarclays;

/// One entry off a Barclaycard statement, in the statement's own terms.
///
/// A card statement prints one amount column and no signs, so direction is
/// carried by <see cref="IsCredit"/> rather than by two money fields the way
/// HSBC's current account does. It is set from the *section* the entry was
/// printed under — "Payments towards your account" — and never from the
/// description: "Payment By Direct Debit" is what the credit happens to be
/// called on these statements, not a rule the bank owes anyone.
public sealed record BarclaysRow
{
    /// ISO. The statement prints "22 Dec" with no year, so the year is inferred
    /// from the statement's own month.
    public required string Date { get; init; }

    public required string Description { get; init; }

    /// As printed, without the £ and with the statement's comma grouping kept.
    public required string Amount { get; init; }

    public required bool IsCredit { get; init; }

    /// "January 2026", off the "issued on" line in the page footer.
    public required string StatementDate { get; init; }
}
