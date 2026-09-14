using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Statements.GetStatements;

public sealed class GetStatementsQuery(IDbConnectionFactory factory)
{
    /// A correlated subquery rather than a GROUP BY: the statement columns are
    /// wide, and grouping by all eleven of them to count a child table reads far
    /// worse than asking for the count where it is used.
    ///
    /// Every bank that stages against a statement file contributes an arm, so
    /// the count is a UNION rather than a column each — the client asks "how many rows came
    /// from this document", not "from which table". A file belongs to one bank,
    /// so only one arm ever contributes to a given statement.
    private const string Sql = $"""
        SELECT  {StatementSql.Columns},
                (SELECT COUNT(*) FROM (
                    SELECT [statementFileId] FROM [AmexTransactions]
                    UNION ALL
                    SELECT [statementFileId] FROM [BarclaysTransactions]
                    UNION ALL
                    SELECT [statementFileId] FROM [HsbcTransactions]
                    UNION ALL
                    SELECT [statementFileId] FROM [SantanderTransactions]
                    UNION ALL
                    SELECT [statementFileId] FROM [ChaseTransactions]
                    UNION ALL
                    SELECT [statementFileId] FROM [SofiTransactions]
                ) r WHERE r.[statementFileId] = s.[id]) AS [stagedRows]
        FROM    [StatementFiles] s
        ORDER BY [statementDate] DESC, [uploadedAt] DESC;
        """;

    public async Task<IReadOnlyList<StatementListItem>> ExecuteAsync(CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<StatementListItem>(
            new CommandDefinition(Sql, cancellationToken: ct));
        return rows.AsList();
    }
}
