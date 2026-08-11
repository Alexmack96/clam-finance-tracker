namespace Clam.Api.Features.Dashboard.GetDashboardAnalytics;

/// Whose Fun budget to show. The client defaults this to whoever is logged in,
/// but either person can switch to view the other's.
public sealed class GetDashboardAnalyticsRequest
{
    public string? Owner { get; set; }
}
