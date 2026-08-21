using FastEndpoints;

namespace Clam.Api.Features.Dashboard.GetDashboardAnalytics;

public sealed class GetDashboardAnalyticsEndpoint(GetDashboardAnalyticsQuery query)
    : Endpoint<GetDashboardAnalyticsRequest, GetDashboardAnalyticsResponse>
{
    public override void Configure()
    {
        Get("dashboard/analytics");
        Description(b => b.WithName("GetDashboardAnalytics"));
    }

    public override async Task HandleAsync(GetDashboardAnalyticsRequest req, CancellationToken ct)
        => await Send.OkAsync(await query.ExecuteAsync(req, ct), ct);
}
