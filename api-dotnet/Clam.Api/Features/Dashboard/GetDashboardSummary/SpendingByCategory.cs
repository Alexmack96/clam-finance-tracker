namespace Clam.Api.Features.Dashboard.GetDashboardSummary;

/// One slice of the dashboard pie. `Value` is a computed running total rather
/// than a stored column, but it stays a `decimal` so it serialises as a string
/// like every other money field the client receives.
public sealed class SpendingByCategory
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public decimal Value { get; set; }
}
