namespace Clam.Api.Features.Tabs.GetTabs;

public sealed class GetTabsResponse
{
    public IReadOnlyList<TabRecord> Tabs { get; set; } = [];
    public TabTotals Totals { get; set; } = new();
}

/// Strings, not numbers: the Express route sends `.toFixed(2)`, and the client
/// renders these directly. Totals cover open tabs only — a settled tab is not
/// money anyone still owes.
public sealed class TabTotals
{
    public string TheyOweMe { get; set; } = "0.00";
    public string IOweThem { get; set; } = "0.00";
}
