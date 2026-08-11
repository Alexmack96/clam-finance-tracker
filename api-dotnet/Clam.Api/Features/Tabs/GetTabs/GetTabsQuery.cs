using System.Globalization;
using Clam.Api.Domain;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Tabs.GetTabs;

public sealed class GetTabsQuery(IDbConnectionFactory factory)
{
    private const string Sql = $"""
        SELECT {TabSql.Columns}
        FROM   [Tabs]
        WHERE  @ShowAll = 1 OR [status] = 'Open'
        ORDER BY [createdAt] DESC;
        """;

    public async Task<GetTabsResponse> ExecuteAsync(GetTabsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var showAll = string.Equals(request.Status, "all", StringComparison.Ordinal);

        using var connection = await factory.OpenAsync(ct);
        var tabs = (await connection.QueryAsync<TabRecord>(
            new CommandDefinition(Sql, new { ShowAll = showAll }, cancellationToken: ct))).AsList();

        // Totalled here rather than in SQL because the list is small and the
        // "open only" rule has to hold even when the query returned settled rows.
        var open = tabs.Where(t => t.Status == TabStatus.Open).ToList();

        return new GetTabsResponse
        {
            Tabs = tabs,
            Totals = new TabTotals
            {
                TheyOweMe = Format(open.Where(t => t.Direction == TabDirection.TheyOwe).Sum(t => t.Amount)),
                IOweThem = Format(open.Where(t => t.Direction == TabDirection.IOwe).Sum(t => t.Amount)),
            },
        };
    }

    private static string Format(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
}
