using System.Globalization;
using Clam.Api.Domain;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Utilities.GetUtilities;

/// The clock is injected because "this month" is the whole of this endpoint's
/// filter — see GetDashboardAnalyticsQuery for the same reasoning.
public sealed class GetUtilitiesQuery(IDbConnectionFactory factory, TimeProvider clock)
{
    /// The household bills, in the order the page shows them. A fixed list, not
    /// a flag on Category: which categories count as "the utilities" is a
    /// property of this page, not of a category.
    private static readonly string[] UtilityNames = ["Rent", "Water", "Wifi", "Electricity", "Council Tax"];

    private const string Sql = """
        SELECT  c.[id], c.[name], c.[color],
                t.[amount], t.[date], t.[description], t.[owner]
        FROM    [Categories] c
        LEFT JOIN [Transactions] t
               ON t.[categoryId] = c.[id]
              AND t.[date] >= @MonthStart
              AND t.[date] <  @MonthEnd
        WHERE   c.[name] IN @Names
        ORDER BY t.[date] ASC;
        """;

    public async Task<GetUtilitiesResponse> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1);

        using var connection = await factory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<UtilityRow>(new CommandDefinition(
            Sql, new { MonthStart = monthStart, MonthEnd = monthEnd, Names = UtilityNames }, cancellationToken: ct)))
            .AsList();

        var byName = rows.ToLookup(r => r.Name, StringComparer.Ordinal);

        // Driven by the fixed list rather than by what the query returned, so
        // display order is stable and a bill nobody has paid yet still appears.
        var utilities = UtilityNames
            .Where(name => byName.Contains(name))
            .Select(name => ToView(byName[name].ToList()))
            .ToList();

        return new GetUtilitiesResponse
        {
            Month = now.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-GB")),
            Utilities = utilities,
        };
    }

    private static UtilityView ToView(List<UtilityRow> rows)
    {
        // The LEFT JOIN gives one all-null row for a category with no payments
        // this month; that is a real category with an empty column, not a row to
        // drop.
        var paid = rows.Where(r => r.Date is not null).ToList();

        return new UtilityView
        {
            Id = rows[0].Id,
            Name = rows[0].Name,
            Color = rows[0].Color,
            Payments = new UtilityPayers
            {
                [nameof(Owner.Alex)] = PaymentsFor(paid, Owner.Alex),
                [nameof(Owner.Casey)] = PaymentsFor(paid, Owner.Casey),
                [nameof(Owner.Joint)] = PaymentsFor(paid, Owner.Joint),
            },
            TotalThisMonth = paid.Sum(r => (double)r.Amount!.Value),
        };
    }

    private static List<UtilityPayment> PaymentsFor(List<UtilityRow> rows, Owner owner) =>
        [.. rows
            .Where(r => r.Owner == owner)
            .Select(r => new UtilityPayment
            {
                Amount = r.Amount!.Value,
                Date = r.Date!.Value,
                Description = r.Description ?? "",
            })];

    private sealed class UtilityRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Color { get; set; } = "";
        public decimal? Amount { get; set; }
        public DateTime? Date { get; set; }
        public string? Description { get; set; }
        public Owner? Owner { get; set; }
    }
}
