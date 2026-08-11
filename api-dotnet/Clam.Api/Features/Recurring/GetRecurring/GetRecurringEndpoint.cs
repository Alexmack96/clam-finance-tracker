using FastEndpoints;

namespace Clam.Api.Features.Recurring.GetRecurring;

public sealed class GetRecurringEndpoint(GetRecurringQuery query)
    : Endpoint<GetRecurringRequest, GetRecurringResponse>
{
    public override void Configure()
    {
        Get("recurring");
        AllowAnonymous();
        Description(b => b.WithName("GetRecurring"));
    }

    public override async Task HandleAsync(GetRecurringRequest req, CancellationToken ct)
        => await Send.OkAsync(await query.ExecuteAsync(req, ct), ct);
}
