namespace Clam.Api.Features.Import.BackfillUsdGbp;

/// One-shot backfill for SoFi and Chase transactions imported before FX
/// conversion existed. Idempotent: rows that already carry an `originalAmount`
/// have been converted and are not candidates.
public sealed class BackfillUsdGbpRequest
{
    /// Preview only — reports what would change without writing anything.
    public bool DryRun { get; set; }
}
