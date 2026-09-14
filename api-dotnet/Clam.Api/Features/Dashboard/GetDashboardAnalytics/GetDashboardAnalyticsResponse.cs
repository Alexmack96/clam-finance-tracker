namespace Clam.Api.Features.Dashboard.GetDashboardAnalytics;

/// This month's Wants spend against the allowance, plus enough calendar to draw
/// a pace line without the client guessing the month length.
public sealed class BudgetGauge
{
    public double Spent { get; set; }
    public double Limit { get; set; }

    /// Three-letter month, matching the labels on every series below.
    public string Month { get; set; } = "";

    public int Day { get; set; }
    public int DaysInMonth { get; set; }
}

/// Volume, not value — how many rows landed each month. Useful as an import
/// sanity check: a month that suddenly drops usually means a statement was never
/// uploaded, which no spending chart would show you.
public sealed class MonthlyCount
{
    public string Month { get; set; } = "";
    public int Count { get; set; }

    /// The current month is still in progress, so its bar is not comparable to
    /// the rest. Flagged here rather than inferred client-side.
    public bool Partial { get; set; }
}

public sealed class MonthlyAmount
{
    public string Month { get; set; } = "";
    public double Amount { get; set; }
}

public sealed class CategorySwatch
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
}

public sealed class CategoryTotal
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public double Value { get; set; }
}

public sealed class OwnerTotal
{
    public string Owner { get; set; } = "";
    public double Amount { get; set; }
}

public sealed class GetDashboardAnalyticsResponse
{
    public BudgetGauge Budget { get; set; } = new();
    public IReadOnlyList<MonthlyCount> MonthlyTransactionCount { get; set; } = [];

    /// One object per month, keyed by category name — `{ month: "Jan",
    /// "Food & Social": 120.5, "Activities": 40 }`. A dictionary rather than a
    /// typed shape because the series are configured by category name, and the
    /// charting component reads the keys off `funCategories`.
    public IReadOnlyList<Dictionary<string, object>> MonthlyFun { get; set; } = [];

    public IReadOnlyList<CategorySwatch> FunCategories { get; set; } = [];
    public IReadOnlyList<MonthlyAmount> MonthlyVacation { get; set; } = [];
    public string VacationColor { get; set; } = "";
    public IReadOnlyList<Dictionary<string, object>> MonthlyFood { get; set; } = [];
    public IReadOnlyList<CategorySwatch> FoodCategories { get; set; } = [];
    public IReadOnlyList<CategoryTotal> SpendingByCategory { get; set; } = [];
    public IReadOnlyList<OwnerTotal> OwnerBreakdown { get; set; } = [];
    public IReadOnlyList<MonthlyAmount> MonthlyGolf { get; set; } = [];

    /// The five monthly charts again, once per owner — keyed `Alex`, `Casey`,
    /// `Joint`. The fields above are the unfiltered "All" view. Sent together
    /// so switching a chart's owner is a lookup rather than a refetch.
    public IReadOnlyDictionary<string, OwnerSeries> ByOwner { get; set; } =
        new Dictionary<string, OwnerSeries>(StringComparer.Ordinal);
}

/// One owner's transactions only, matched exactly. Unlike the budget gauge there
/// is no weighting: Joint is the joint account's rows and Alex excludes them, so
/// the three owners add up to the unfiltered series.
public sealed class OwnerSeries
{
    public IReadOnlyList<MonthlyCount> MonthlyTransactionCount { get; set; } = [];
    public IReadOnlyList<Dictionary<string, object>> MonthlyFun { get; set; } = [];
    public IReadOnlyList<MonthlyAmount> MonthlyVacation { get; set; } = [];
    public IReadOnlyList<Dictionary<string, object>> MonthlyFood { get; set; } = [];
    public IReadOnlyList<MonthlyAmount> MonthlyGolf { get; set; } = [];
}
