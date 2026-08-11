namespace Clam.Api.Features.Import.ProcessStaged;

/// The tallies of one run. `Processed` counts rows the pipeline finished with,
/// including ones whose transaction already existed — the row moved out of
/// pending either way, and reporting it as anything else would suggest work
/// still to do.
public sealed class ProcessStagedResponse
{
    public int Processed { get; set; }
    public int Skipped { get; set; }
    public int Errored { get; set; }
}
