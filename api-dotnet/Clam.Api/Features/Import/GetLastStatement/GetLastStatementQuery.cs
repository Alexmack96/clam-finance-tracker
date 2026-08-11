using System.Globalization;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Import.GetLastStatement;

public sealed class GetLastStatementQuery(IDbConnectionFactory factory)
{
    /// Grouped on the `externalId` namespace rather than joined to a bank table,
    /// because that prefix is the only thing a processed transaction still
    /// carries about where it came from.
    ///
    /// LEFT of the colon, so `flex:` rows group under `flex` and never inflate
    /// Monzo's date — they are a different card on the same login.
    private const string Sql = """
        SELECT  LEFT([externalId], CHARINDEX(':', [externalId]) - 1) AS [bank],
                MAX([date]) AS [latest]
        FROM    [Transactions]
        WHERE   [externalId] IS NOT NULL AND CHARINDEX(':', [externalId]) > 1
        GROUP BY LEFT([externalId], CHARINDEX(':', [externalId]) - 1);

        SELECT  [owner], MAX([date]) AS [latest]
        FROM    [Transactions]
        WHERE   [externalId] LIKE 'amex:%'
        GROUP BY [owner];
        """;

    public async Task<GetLastStatementResponse> ExecuteAsync(CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(Sql, cancellationToken: ct));

        var byBank = (await grid.ReadAsync<(string Bank, DateTime? Latest)>())
            .ToDictionary(r => r.Bank, r => r.Latest, StringComparer.Ordinal);

        var byOwner = await grid.ReadAsync<(string Owner, DateTime? Latest)>();

        return new GetLastStatementResponse
        {
            Monzo = DateOnlyOrNull(byBank, "monzo"),
            Amex = DateOnlyOrNull(byBank, "amex"),
            Barclays = DateOnlyOrNull(byBank, "barclays"),
            Santander = DateOnlyOrNull(byBank, "santander"),
            Hsbc = DateOnlyOrNull(byBank, "hsbc"),
            Sofi = DateOnlyOrNull(byBank, "sofi"),
            Chase = DateOnlyOrNull(byBank, "chase"),
            AmexByOwner = byOwner.ToDictionary(r => r.Owner, r => Format(r.Latest), StringComparer.Ordinal),
        };
    }

    private static string? DateOnlyOrNull(Dictionary<string, DateTime?> byBank, string bank) =>
        byBank.TryGetValue(bank, out var latest) ? Format(latest) : null;

    private static string? Format(DateTime? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
