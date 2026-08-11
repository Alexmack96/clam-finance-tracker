namespace Clam.Api.Features.Import.GetStagedCounts;

/// How many staged rows sit in each state.
public sealed class StagedCounts
{
    public int Pending { get; set; }
    public int Processed { get; set; }
    public int Skipped { get; set; }
    public int Errored { get; set; }

    /// The same four counts broken down by owner, keyed by owner name. Amex in
    /// particular is shared between two people, so a single pending count would
    /// not tell you whose statement is waiting.
    ///
    /// Absent on Monzo, which is a single synced feed with no per-owner split
    /// until the process step assigns one.
    public Dictionary<string, Dictionary<string, int>>? ByOwner { get; set; }
}

public sealed class GetStagedCountsResponse
{
    public StagedCounts Monzo { get; set; } = new();
    public StagedCounts Amex { get; set; } = new();
    public StagedCounts Barclays { get; set; } = new();
    public StagedCounts Santander { get; set; } = new();
    public StagedCounts Hsbc { get; set; } = new();
    public StagedCounts Sofi { get; set; } = new();
    public StagedCounts Chase { get; set; } = new();
}
