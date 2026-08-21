using FastEndpoints;

namespace Clam.Api.Features.Dashboard.GetDashboardSummary;

public sealed class GetDashboardSummaryEndpoint(GetDashboardSummaryQuery query)
    : EndpointWithoutRequest<GetDashboardSummaryResponse>
{
    public override void Configure()
    {
        Get("dashboard/summary");
        Description(b => b.WithName("GetDashboardSummary"));
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await Send.OkAsync(await query.ExecuteAsync(ct), ct);
}
