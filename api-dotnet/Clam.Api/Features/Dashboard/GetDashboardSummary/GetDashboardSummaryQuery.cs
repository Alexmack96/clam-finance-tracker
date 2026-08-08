using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Dashboard.GetDashboardSummary;

public sealed class GetDashboardSummaryQuery(IDbConnectionFactory factory)
{
    // routes/dashboard.ts pulls every transaction into memory and loops. The same
    // result falls out of two aggregates, so it is expressed as SQL here — that is
    // the whole reason for choosing Dapper over an ORM.
    //
    // The Express version guards the Joint branch with `else if`, so a row counted
    // as Casey's income cannot also count as a Joint expense. That guard is
    // unnecessary rather than missing: `owner` is a single column, so a row is
    // never both 'Casey' and 'Joint'. The two CASEs are disjoint by construction.
    private const string Sql = """
        SELECT
            ISNULL(SUM(CASE
                WHEN c.[name] = 'Net'
                 AND t.[owner] = 'Casey'
                 AND t.[type] = 'Income'
                 AND t.[externalId] LIKE 'monzo:%'
                THEN t.[amount]
            END), 0) AS [caseyIn],
            ISNULL(SUM(CASE
                WHEN t.[owner] = 'Joint'
                THEN CASE WHEN t.[type] = 'Expense' THEN t.[amount] ELSE -t.[amount] END
            END), 0) AS [jointExpenses]
        FROM [Transaction] t
        JOIN [Category] c ON c.[id] = t.[categoryId];

        SELECT      c.[name],
                    c.[color],
                    SUM(t.[amount]) AS [value]
        FROM        [Transaction] t
        JOIN        [Category] c ON c.[id] = t.[categoryId]
        WHERE       t.[owner] = 'Joint'
          AND       t.[type]  = 'Expense'
        GROUP BY    c.[id], c.[name], c.[color]
        ORDER BY    [value] DESC;
        """;

    public async Task<GetDashboardSummaryResponse> ExecuteAsync(CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);
        using var results = await connection.QueryMultipleAsync(
            new CommandDefinition(Sql, cancellationToken: ct));

        var totals = await results.ReadSingleAsync<SummaryTotals>();
        var spending = (await results.ReadAsync<SpendingByCategory>()).ToList();

        return new GetDashboardSummaryResponse
        {
            CaseyIn = totals.CaseyIn,
            JointExpenses = totals.JointExpenses,
            Settlement = totals.CaseyIn - totals.JointExpenses / 2,
            SpendingByCategory = spending,
        };
    }
}
