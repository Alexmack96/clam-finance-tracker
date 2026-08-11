namespace Clam.Api.Features.Import.BackfillUsdGbp;

public sealed class BackfillUsdGbpResponse
{
    public int Candidates { get; set; }
    public int Converted { get; set; }
    public int Errored { get; set; }

    /// Capped at the first 20. A backfill that fails on 900 rows fails for one
    /// reason, and the response is meant to be readable.
    public IReadOnlyList<string> Errors { get; set; } = [];

    public bool DryRun { get; set; }
}
