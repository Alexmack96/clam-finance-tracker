using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Import.GetStagedCounts;

public sealed class GetStagedCountsQuery(IDbConnectionFactory factory)
{
    /// One UNION ALL over every staging table rather than seven round trips.
    /// The table names are compile-time constants from <see cref="StagingBanks"/>,
    /// never anything a caller supplied — this is the one place where
    /// interpolating an identifier into SQL is safe, and it stays that way only
    /// because that list is the sole source.
    ///
    /// Monzo has no `owner` column: it is a single synced feed, and ownership is
    /// resolved from the merchant name during processing.
    private static readonly string Sql = string.Join(
        "\nUNION ALL\n",
        StagingBanks.All.Select(b => b.Bank == "monzo"
            ? $"SELECT '{b.Bank}' AS [bank], [status], NULL AS [owner], COUNT(*) AS [count] FROM [{b.Table}] GROUP BY [status]"
            : $"SELECT '{b.Bank}' AS [bank], [status], [owner], COUNT(*) AS [count] FROM [{b.Table}] GROUP BY [status], [owner]"));

    public async Task<GetStagedCountsResponse> ExecuteAsync(CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<StagedCountRow>(
            new CommandDefinition(Sql, cancellationToken: ct))).AsList();

        return new GetStagedCountsResponse
        {
            Monzo = Totals(rows, "monzo", withOwners: false),
            Amex = Totals(rows, "amex", withOwners: true),
            Barclays = Totals(rows, "barclays", withOwners: true),
            Santander = Totals(rows, "santander", withOwners: true),
            Hsbc = Totals(rows, "hsbc", withOwners: true),
            Sofi = Totals(rows, "sofi", withOwners: true),
            Chase = Totals(rows, "chase", withOwners: true),
        };
    }

    private static StagedCounts Totals(List<StagedCountRow> rows, string bank, bool withOwners)
    {
        var forBank = rows.Where(r => string.Equals(r.Bank, bank, StringComparison.Ordinal)).ToList();

        var counts = new StagedCounts
        {
            Pending = Sum(forBank, StagedStatus.Pending),
            Processed = Sum(forBank, StagedStatus.Processed),
            Skipped = Sum(forBank, StagedStatus.Skipped),
            Errored = Sum(forBank, StagedStatus.Errored),
        };

        if (withOwners)
        {
            counts.ByOwner = forBank
                .Where(r => r.Owner is not null)
                .GroupBy(r => r.Owner!, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(r => r.Status, StringComparer.Ordinal)
                          .ToDictionary(s => s.Key, s => s.Sum(r => r.Count), StringComparer.Ordinal),
                    StringComparer.Ordinal);
        }

        return counts;
    }

    private static int Sum(List<StagedCountRow> rows, string status) =>
        rows.Where(r => string.Equals(r.Status, status, StringComparison.Ordinal)).Sum(r => r.Count);

    private sealed class StagedCountRow
    {
        public string Bank { get; set; } = "";
        public string Status { get; set; } = "";
        public string? Owner { get; set; }
        public int Count { get; set; }
    }
}
