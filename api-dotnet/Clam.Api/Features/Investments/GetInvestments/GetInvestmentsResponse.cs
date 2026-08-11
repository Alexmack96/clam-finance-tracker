namespace Clam.Api.Features.Investments.GetInvestments;

/// An account as the page renders it — no owner, no timestamps. The page is
/// already scoped to one owner, and nothing on it shows when a row was created.
public sealed class InvestmentAccountView
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public double? Rate { get; set; }
    public int SortOrder { get; set; }
    public IReadOnlyList<InvestmentSnapshotView> Snapshots { get; set; } = [];
}

public sealed class InvestmentSnapshotView
{
    public string Id { get; set; } = "";
    public DateTime Date { get; set; }
    public double Value { get; set; }
}

/// NAV excludes pension deliberately — it is illiquid and tracked on its own
/// line, so mixing it in would make every P&L figure unspendable.
public sealed class InvestmentStats
{
    public double NavLatest { get; set; }
    public double NavPrev { get; set; }
    public double DtdPnL { get; set; }
    public double MtdPnL { get; set; }
    public double YtdPnL { get; set; }
    public double ItdPnL { get; set; }

    /// Null until a pension account has at least one snapshot.
    public double? Pension { get; set; }

    public double? PensionPrev { get; set; }

    /// Null when the pension is unknown: NAV alone is not total wealth, and
    /// showing it as if it were would understate the number.
    public double? TotalWealth { get; set; }
}

public sealed class GetInvestmentsResponse
{
    public IReadOnlyList<InvestmentAccountView> Accounts { get; set; } = [];

    /// Every distinct snapshot date across all accounts, ascending. The page
    /// renders one column per date, so accounts missing a date show a gap
    /// rather than shifting left.
    public IReadOnlyList<DateTime> Dates { get; set; } = [];

    public InvestmentStats Stats { get; set; } = new();
}
