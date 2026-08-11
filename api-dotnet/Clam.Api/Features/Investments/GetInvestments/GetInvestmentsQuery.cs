using System.Globalization;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Investments.GetInvestments;

/// The clock is injected because the MTD and YTD baselines are picked relative
/// to "now" — see GetDashboardAnalyticsQuery for the same reasoning.
public sealed class GetInvestmentsQuery(IDbConnectionFactory factory, TimeProvider clock)
{
    private const string PensionCategory = "pension";

    private const string Sql = """
        SELECT  a.[id], a.[name], a.[category], a.[rate], a.[sortOrder]
        FROM    [InvestmentAccounts] a
        WHERE   a.[owner] = @Owner
        ORDER BY a.[sortOrder] ASC;

        SELECT  s.[id], s.[accountId], s.[date], s.[value]
        FROM    [InvestmentSnapshots] s
        JOIN    [InvestmentAccounts] a ON a.[id] = s.[accountId]
        WHERE   a.[owner] = @Owner
        ORDER BY s.[date] ASC;
        """;

    public async Task<GetInvestmentsResponse> ExecuteAsync(GetInvestmentsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owner = InvestmentOwner.Parse(request.Owner);

        using var connection = await factory.OpenAsync(ct);
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(Sql, new { Owner = owner.ToString() }, cancellationToken: ct));

        var accounts = (await grid.ReadAsync<InvestmentAccountView>()).AsList();
        var snapshots = (await grid.ReadAsync<SnapshotRow>()).AsList();

        var byAccount = snapshots.ToLookup(s => s.AccountId, StringComparer.Ordinal);
        foreach (var account in accounts)
        {
            account.Snapshots =
            [
                .. byAccount[account.Id].Select(s => new InvestmentSnapshotView
                {
                    Id = s.Id,
                    Date = s.Date,
                    Value = s.Value,
                })
            ];
        }

        var dates = snapshots.Select(s => s.Date).Distinct().Order().ToList();

        return new GetInvestmentsResponse
        {
            Accounts = accounts,
            Dates = dates,
            Stats = BuildStats(accounts, dates, clock.GetUtcNow().UtcDateTime),
        };
    }

    private static InvestmentStats BuildStats(
        List<InvestmentAccountView> accounts,
        List<DateTime> dates,
        DateTime now)
    {
        var currentMonth = YearMonth(now);
        var previousMonth = YearMonth(new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1));
        var previousYear = (now.Year - 1).ToString(CultureInfo.InvariantCulture);

        var latestDate = dates.Count > 0 ? dates[^1] : (DateTime?)null;
        var prevDate = dates.Count > 1 ? dates[^2] : (DateTime?)null;

        // MTD base: the last snapshot in the previous calendar month (Mar 31
        // when we are in April). Falls back to the last one before this month at
        // all, so a month with no snapshots does not blank the figure.
        var mtdBase =
            LastWhere(dates, d => YearMonth(d) == previousMonth)
            ?? LastWhere(dates, d => string.CompareOrdinal(YearMonth(d), currentMonth) < 0);

        // YTD base: last snapshot in December of the previous year; falls back
        // to the last snapshot of that year if there is no December entry.
        var ytdBase =
            LastWhere(dates, d => YearMonth(d) == $"{previousYear}-12")
            ?? LastWhere(dates, d => d.Year == now.Year - 1);

        var itdBase = dates.Count > 0 ? dates[0] : (DateTime?)null;

        var navLatest = Nav(accounts, latestDate);
        var navPrev = Nav(accounts, prevDate);

        // Pension is read off its own account's last two snapshots rather than
        // the shared date axis: it is updated on its own cadence and is usually
        // missing from the most recent column.
        var pensionSnapshots = accounts
            .FirstOrDefault(a => string.Equals(a.Category, PensionCategory, StringComparison.Ordinal))
            ?.Snapshots ?? [];

        var latestPension = pensionSnapshots.Count > 0 ? pensionSnapshots[^1].Value : (double?)null;
        var prevPension = pensionSnapshots.Count > 1 ? pensionSnapshots[^2].Value : (double?)null;

        return new InvestmentStats
        {
            NavLatest = navLatest,
            NavPrev = navPrev,
            DtdPnL = navLatest - navPrev,
            MtdPnL = navLatest - Nav(accounts, mtdBase),
            YtdPnL = navLatest - Nav(accounts, ytdBase),
            ItdPnL = navLatest - Nav(accounts, itdBase),
            Pension = latestPension,
            PensionPrev = prevPension,
            TotalWealth = latestPension is null ? null : navLatest + latestPension,
        };
    }

    /// Sum of every non-pension account's value on exactly this date. An account
    /// with no snapshot that day contributes nothing rather than carrying its
    /// last known value forward — the page shows what was actually recorded.
    private static double Nav(List<InvestmentAccountView> accounts, DateTime? date)
    {
        if (date is null) return 0;

        return accounts
            .Where(a => !string.Equals(a.Category, PensionCategory, StringComparison.Ordinal))
            .Sum(a => a.Snapshots.FirstOrDefault(s => s.Date == date.Value)?.Value ?? 0);
    }

    private static DateTime? LastWhere(List<DateTime> dates, Func<DateTime, bool> predicate)
    {
        for (var i = dates.Count - 1; i >= 0; i--)
        {
            if (predicate(dates[i])) return dates[i];
        }
        return null;
    }

    private static string YearMonth(DateTime date) =>
        date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private sealed class SnapshotRow
    {
        public string Id { get; set; } = "";
        public string AccountId { get; set; } = "";
        public DateTime Date { get; set; }
        public double Value { get; set; }
    }
}
