using FastEndpoints;

namespace Clam.Api.Features.Monzo.GetFlexRec;

public sealed class GetFlexRecEndpoint(GetFlexRecQuery query)
    : EndpointWithoutRequest<GetFlexRecResponse>
{
    public override void Configure()
    {
        Get("admin/flex/rec");
        Description(b => b.WithName("GetFlexRec"));
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await Send.OkAsync(await query.ExecuteAsync(ct), ct);
}
