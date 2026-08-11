using FastEndpoints;

namespace Clam.Api.Features.Utilities.GetUtilities;

public sealed class GetUtilitiesEndpoint(GetUtilitiesQuery query) : EndpointWithoutRequest<GetUtilitiesResponse>
{
    public override void Configure()
    {
        Get("utilities");
        AllowAnonymous();
        Description(b => b.WithName("GetUtilities"));
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await Send.OkAsync(await query.ExecuteAsync(ct), ct);
}
