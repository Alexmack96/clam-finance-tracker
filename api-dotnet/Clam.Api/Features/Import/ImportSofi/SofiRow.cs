namespace Clam.Api.Features.Import.ImportSofi;

/// One entry off a SoFi Money statement, in the statement's own terms.
///
/// The only staged row of any bank that carries an identifier the bank itself
/// printed: SoFi puts "Transaction ID: 485-496003001" under every entry, and
/// those ids are unique across accounts and across statements. Every other bank
/// here is keyed on a hash of the row's content because it gives you nothing to
/// key on.
public sealed record SofiRow
{
    /// SoFi's own, off the "Transaction ID:" line under the entry.
    public required string TransactionId { get; init; }

    /// ISO. Unlike the card statements, SoFi prints the year on every row, so
    /// nothing is inferred.
    public required string Date { get; init; }

    /// "Interest Earned", "Direct Payment", "Deposit", "Withdrawal", "Instant
    /// Transfer". Taken from the TYPE column verbatim rather than matched
    /// against a list — the column is positional, so a type this code has never
    /// seen is read correctly instead of dropping the row.
    public required string Type { get; init; }

    public required string Description { get; init; }

    /// USD, unsigned. The sign lives in <see cref="IsCredit"/> — the process
    /// step converts this figure to sterling and a negative would come back as a
    /// negative expense.
    public required string Amount { get; init; }

    public required bool IsCredit { get; init; }

    /// The running balance after this entry, as printed.
    public required string Balance { get; init; }

    /// "Checking" or "Savings". One PDF holds both accounts, one after the
    /// other, and the transfers between them are what the process step skips —
    /// importing those would count the same dollars twice.
    public required string AccountType { get; init; }

    /// "Feb 2026", off the statement period's end.
    public required string StatementDate { get; init; }
}
