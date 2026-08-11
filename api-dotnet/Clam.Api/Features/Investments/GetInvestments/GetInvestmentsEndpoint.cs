using FastEndpoints;

namespace Clam.Api.Features.Investments.GetInvestments;

public sealed class GetInvestmentsEndpoint(GetInvestmentsQuery query)
    : Endpoint<GetInvestmentsRequest, GetInvestmentsResponse>
{
    public override void Configure()
    {
        Get("investments");
        AllowAnonymous();
        Description(b => b.WithName("GetInvestments"));
    }

    public override async Task HandleAsync(GetInvestmentsRequest req, CancellationToken ct)
        => await Send.OkAsync(await query.ExecuteAsync(req, ct), ct);
}
