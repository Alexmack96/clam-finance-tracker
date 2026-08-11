using Clam.Api.Domain;

namespace Clam.Api.Features.Import.ProcessStaged;

/// A staged row after normalisation — what every bank's block funnels into, and
/// the only shape the insert below understands.
///
/// Slice-local, and it stays that way: this is the pipeline's internal currency,
/// not something a caller ever sees.
internal sealed class NormalisedTransaction
{
    public required string ExternalId { get; init; }
    public required string Description { get; init; }
    public required decimal Amount { get; init; }
    public required TransactionType Type { get; init; }
    public required DateTime Date { get; init; }
    public required string CategoryId { get; init; }
    public required Bucket? Bucket { get; init; }
    public required string Owner { get; init; }

    /// Set only for banks whose rows trace back to a stored PDF.
    public string? StatementFileId { get; init; }

    /// The pre-conversion figure, for the two USD banks. Null elsewhere, which
    /// is how "this amount was always sterling" is recorded.
    public decimal? OriginalAmount { get; init; }

    public string? OriginalCurrency { get; init; }
}
