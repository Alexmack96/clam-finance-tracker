using FastEndpoints;

namespace Clam.Api.Features.Monzo.GetMonzoRecRuns;

public sealed class GetMonzoRecRunsEndpoint(GetMonzoRecRunsQuery query)
    : EndpointWithoutRequest<GetMonzoRecRunsResponse>
{
    public override void Configure()
    {
        Get("admin/monzo/rec");
        Description(b => b.WithName("GetMonzoRecRuns"));
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await Send.OkAsync(new GetMonzoRecRunsResponse(await query.ExecuteAsync(ct)), ct);
}
