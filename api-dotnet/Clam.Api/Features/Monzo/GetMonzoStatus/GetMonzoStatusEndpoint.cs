using FastEndpoints;

namespace Clam.Api.Features.Monzo.GetMonzoStatus;

public sealed class GetMonzoStatusEndpoint(GetMonzoStatusQuery query)
    : EndpointWithoutRequest<GetMonzoStatusResponse>
{
    public override void Configure()
    {
        Get("admin/monzo/status");
        Description(b => b.WithName("GetMonzoStatus"));
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await Send.OkAsync(await query.ExecuteAsync(ct), ct);
}
