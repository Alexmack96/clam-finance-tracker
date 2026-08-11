namespace Clam.Api.Features.Monzo.GetMonzoRecRuns;

/// A bare JSON array, matching `res.json(runs.map(...))`.
public sealed class GetMonzoRecRunsResponse : List<MonzoRecRun>
{
    public GetMonzoRecRunsResponse(IEnumerable<MonzoRecRun> runs) : base(runs) { }
}
