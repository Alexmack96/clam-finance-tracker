using Clam.Api.Domain;
using Clam.Api.Features.Investments;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Dashboard.GetDashboardAnalytics;

/// Takes a <see cref="TimeProvider"/> rather than reading DateTime.UtcNow: every
/// series here is "so far this year, by month", so the clock is an input to the
/// response and a test that cannot fix it can only assert vaguely.
public sealed class GetDashboardAnalyticsQuery(IDbConnectionFactory factory, TimeProvider clock)
{
    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// "Wants" in the 50/30/20 split is the fun allowance, and these two
    /// categories are what it is spent on.
    private static readonly string[] FunCategories = ["Food & Social", "Activities"];

    private static readonly string[] FoodCategories = ["Food & Social", "Groceries", "Takeout"];

    private const string GolfCategory = "Golf";
    private const string VacationCategory = "Vacation";
    private const string SalaryCategory = "Salary";

    /// Fallback for when no per-person salary can be identified — for example
    /// before any Salary-categorised income has landed.
    private const double FallbackFunBudget = 1650;

    private const string DefaultSwatch = "#888";
    private const string DefaultVacationColor = "#eab308";

    private const string Sql = """
        SELECT  t.[date], t.[amount], t.[type], t.[owner], t.[bucket],
                t.[categoryId], c.[name] AS [categoryName], c.[color] AS [categoryColor]
        FROM    [Transactions] t
        JOIN    [Categories] c ON c.[id] = t.[categoryId]
        WHERE   t.[date] >= @YearStart
        ORDER BY t.[date] ASC;

        SELECT  [name], [color] FROM [Categories];

        SELECT  TOP 1 t.[amount]
        FROM    [Transactions] t
        JOIN    [Categories] c ON c.[id] = t.[categoryId]
        WHERE   c.[name] = @SalaryCategory
          AND   t.[owner] = @Owner
          AND   t.[type] = 'Income'
        ORDER BY t.[date] DESC;
        """;

    public async Task<GetDashboardAnalyticsResponse> ExecuteAsync(
        GetDashboardAnalyticsRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owner = InvestmentOwner.Parse(request.Owner);
        var now = clock.GetUtcNow().UtcDateTime;
        var yearStart = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var currentMonthIndex = now.Month - 1;

        using var connection = await factory.OpenAsync(ct);
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            Sql,
            new { YearStart = yearStart, SalaryCategory, Owner = owner.ToString() },
            cancellationToken: ct));

        var transactions = (await grid.ReadAsync<AnalyticsRow>()).AsList();
        var colorByCategory = (await grid.ReadAsync<(string Name, string Color)>())
            .ToDictionary(c => c.Name, c => c.Color, StringComparer.Ordinal);
        var latestSalary = await grid.ReadSingleOrDefaultAsync<decimal?>();

        var expenses = transactions.Where(t => t.Type == TransactionType.Expense).ToList();
        var monthLabels = Months[..(currentMonthIndex + 1)];

        return new GetDashboardAnalyticsResponse
        {
            Budget = BuildBudget(transactions, owner, latestSalary, now, currentMonthIndex),

            MonthlyTransactionCount = [.. MonthlyCounts(transactions, monthLabels, currentMonthIndex)],

            MonthlyFun = ByMonthAndCategory(expenses, monthLabels, FunCategories),
            FunCategories = [.. Swatches(colorByCategory, FunCategories)],
            MonthlyVacation = [.. MonthlyTotals(expenses, monthLabels, VacationCategory)],
            VacationColor = colorByCategory.GetValueOrDefault(VacationCategory, DefaultVacationColor),
            MonthlyFood = ByMonthAndCategory(expenses, monthLabels, FoodCategories),
            FoodCategories = [.. Swatches(colorByCategory, FoodCategories)],

            SpendingByCategory = [.. expenses
                .GroupBy(t => t.CategoryId, StringComparer.Ordinal)
                .Select(g => new CategoryTotal
                {
                    Name = g.First().CategoryName,
                    Color = g.First().CategoryColor,
                    Value = g.Sum(t => (double)t.Amount),
                })
                .OrderByDescending(c => c.Value)],

            OwnerBreakdown = [.. expenses
                .GroupBy(t => t.Owner)
                .Select(g => new OwnerTotal { Owner = g.Key.ToString(), Amount = g.Sum(t => (double)t.Amount) })
                .Where(o => o.Amount > 0)],

            MonthlyGolf = [.. MonthlyTotals(expenses, monthLabels, GolfCategory)],

            ByOwner = Enum.GetValues<Owner>().ToDictionary(
                o => o.ToString(),
                o => BuildOwnerSeries(
                    [.. transactions.Where(t => t.Owner == o)], monthLabels, currentMonthIndex),
                StringComparer.Ordinal),
        };
    }

    private static OwnerSeries BuildOwnerSeries(
        List<AnalyticsRow> transactions,
        string[] monthLabels,
        int currentMonthIndex)
    {
        var expenses = transactions.Where(t => t.Type == TransactionType.Expense).ToList();

        return new OwnerSeries
        {
            MonthlyTransactionCount = [.. MonthlyCounts(transactions, monthLabels, currentMonthIndex)],
            MonthlyFun = ByMonthAndCategory(expenses, monthLabels, FunCategories),
            MonthlyVacation = [.. MonthlyTotals(expenses, monthLabels, VacationCategory)],
            MonthlyFood = ByMonthAndCategory(expenses, monthLabels, FoodCategories),
            MonthlyGolf = [.. MonthlyTotals(expenses, monthLabels, GolfCategory)],
        };
    }

    private static IEnumerable<MonthlyCount> MonthlyCounts(
        List<AnalyticsRow> transactions,
        string[] monthLabels,
        int currentMonthIndex) =>
        monthLabels.Select((month, i) => new MonthlyCount
        {
            Month = month,
            Count = transactions.Count(t => t.Date.Month - 1 == i),
            Partial = i == currentMonthIndex,
        });

    /// Wants budget = net Wants spend this month, owner-weighted like the
    /// savings score: the selected person in full plus half of Joint. Refunds
    /// (income tagged Wants) net off, so this reads all transactions rather than
    /// just expenses.
    private static BudgetGauge BuildBudget(
        List<AnalyticsRow> transactions,
        Owner owner,
        decimal? latestSalary,
        DateTime now,
        int currentMonthIndex)
    {
        // 30% of the latest salary — the "wants" third of the 50/30/20 split.
        var limit = latestSalary is null
            ? FallbackFunBudget
            : Math.Round((double)latestSalary.Value * 0.3, MidpointRounding.AwayFromZero);

        var thisMonth = transactions.Where(t => t.Date.Month - 1 == currentMonthIndex);

        return new BudgetGauge
        {
            Spent = BucketMath.NetBucketSpent(
                thisMonth, Bucket.Wants, owner, t => (t.Owner, t.Type, (double)t.Amount, t.Bucket)),
            Limit = limit,
            Month = Months[currentMonthIndex],
            Day = now.Day,
            DaysInMonth = DateTime.DaysInMonth(now.Year, now.Month),
        };
    }

    /// One row per month, one key per category. A category the database does not
    /// have still gets a key at 0 — dropping it would make the chart's series
    /// disappear rather than flatline.
    private static List<Dictionary<string, object>> ByMonthAndCategory(
        List<AnalyticsRow> expenses,
        string[] monthLabels,
        string[] categories) =>
        [.. monthLabels.Select((month, i) =>
        {
            var entry = new Dictionary<string, object>(StringComparer.Ordinal) { ["month"] = month };
            foreach (var name in categories)
            {
                entry[name] = expenses
                    .Where(t => t.Date.Month - 1 == i && string.Equals(t.CategoryName, name, StringComparison.Ordinal))
                    .Sum(t => (double)t.Amount);
            }
            return entry;
        })];

    private static IEnumerable<MonthlyAmount> MonthlyTotals(
        List<AnalyticsRow> expenses,
        string[] monthLabels,
        string categoryName) =>
        monthLabels.Select((month, i) => new MonthlyAmount
        {
            Month = month,
            Amount = expenses
                .Where(t => t.Date.Month - 1 == i && string.Equals(t.CategoryName, categoryName, StringComparison.Ordinal))
                .Sum(t => (double)t.Amount),
        });

    private static IEnumerable<CategorySwatch> Swatches(
        Dictionary<string, string> colorByCategory,
        string[] names) =>
        names.Select(name => new CategorySwatch
        {
            Name = name,
            Color = colorByCategory.GetValueOrDefault(name, DefaultSwatch),
        });

    private sealed class AnalyticsRow
    {
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public TransactionType Type { get; set; }
        public Owner Owner { get; set; }
        public Bucket? Bucket { get; set; }
        public string CategoryId { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public string CategoryColor { get; set; } = "";
    }
}
