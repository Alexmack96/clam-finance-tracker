namespace Clam.Api.Features.Dashboard.GetDashboardSummary;

/// Matches the object `dashboardRouter.get("/summary")` returns, key for key.
public sealed class GetDashboardSummaryResponse
{
    public decimal CaseyIn { get; set; }
    public decimal JointExpenses { get; set; }

    /// What Casey is owed, or owes: her contributions less half the joint spend.
    public decimal Settlement { get; set; }

    public IReadOnlyList<SpendingByCategory> SpendingByCategory { get; set; } = [];
}
