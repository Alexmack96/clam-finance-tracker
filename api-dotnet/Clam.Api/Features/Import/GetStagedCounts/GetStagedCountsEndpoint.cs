using FastEndpoints;

namespace Clam.Api.Features.Import.GetStagedCounts;

public sealed class GetStagedCountsEndpoint(GetStagedCountsQuery query)
    : EndpointWithoutRequest<GetStagedCountsResponse>
{
    public override void Configure()
    {
        Get("admin/staged");
        AllowAnonymous();
        Description(b => b.WithName("GetStagedCounts"));
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await Send.OkAsync(await query.ExecuteAsync(ct), ct);
}
