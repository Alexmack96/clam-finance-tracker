namespace Clam.Api.Features.Import.ImportAmex;

/// One transaction line off an Amex statement, in the statement's own terms:
/// strings, not decimals, and the printed currency *name* rather than an ISO
/// code. Staging keeps what the document said; normalisation happens later, in
/// the process step, so a parser fix never has to fight a conversion.
public sealed record AmexRow
{
    public required string TransactionDate { get; init; }
    public required string ProcessDate { get; init; }
    public required string Description { get; init; }
    public required string Amount { get; init; }

    /// Amex prints "CR" on its own line under the amount it qualifies, so this
    /// is only final once the whole page has been read.
    public bool IsCredit { get; set; }

    /// Set from the continuation line under a foreign row ("MALAYSIAN RINGGIT").
    public string? ForeignCurrency { get; set; }

    public string? ForeignAmount { get; init; }

    public required string StatementDate { get; init; }
}
