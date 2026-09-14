using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Statements.GetStatement;

public sealed class GetStatementQuery(IDbConnectionFactory factory)
{
    /// The rows are a UNION over every bank that stages against a statement
    /// file, not a branch on <c>bank</c>: a file belongs to one bank, so the
    /// other arm contributes nothing and there is no case to keep in step with
    /// the column. Each arm pads the columns it has no answer for.
    ///
    /// HSBC's direction is the column its figure was printed in — that is the
    /// whole of it, and re-deriving it from the payment type here would undo
    /// the one thing the parser is careful about. Barclaycard's is the section
    /// the entry was printed under, decided the same way and for the same
    /// reason, and is already a column by the time it gets here.
    private const string Sql = $"""
        SELECT  {StatementSql.Columns},
                (SELECT COUNT(*) FROM [Transactions] t WHERE t.[statementFileId] = s.[id]) AS [transactions]
        FROM    [StatementFiles] s
        WHERE   s.[id] = @Id;

        SELECT  [transactionId], [transactionDate], [processDate], [description], [amount],
                [isCredit], [foreignCurrency], [foreignAmount],
                CAST(NULL AS NVARCHAR(60)) AS [paymentType],
                CAST(NULL AS NVARCHAR(40)) AS [balance],
                [statementDate], [owner], [importedAt], [status], [statementFileId]
        FROM    [AmexTransactions]
        WHERE   [statementFileId] = @Id
        UNION ALL
        SELECT  [transactionId], [date], CAST(NULL AS NVARCHAR(20)), [description],
                [amount], [isCredit],
                CAST(NULL AS NVARCHAR(10)), CAST(NULL AS NVARCHAR(40)),
                CAST(NULL AS NVARCHAR(60)), CAST(NULL AS NVARCHAR(40)),
                [statementDate], [owner], [importedAt], [status], [statementFileId]
        FROM    [BarclaysTransactions]
        WHERE   [statementFileId] = @Id
        UNION ALL
        SELECT  [transactionId], [date], CAST(NULL AS NVARCHAR(20)), [description],
                COALESCE([moneyOut], [moneyIn], ''),
                CAST(CASE WHEN [moneyIn] IS NOT NULL THEN 1 ELSE 0 END AS BIT),
                CAST(NULL AS NVARCHAR(10)), CAST(NULL AS NVARCHAR(40)),
                [paymentType], [balance],
                [statementDate], [owner], [importedAt], [status], [statementFileId]
        FROM    [HsbcTransactions]
        WHERE   [statementFileId] = @Id
        UNION ALL
        SELECT  [transactionId], [date], CAST(NULL AS NVARCHAR(20)), [description],
                COALESCE([moneyOut], [moneyIn], ''),
                CAST(CASE WHEN [moneyIn] IS NOT NULL THEN 1 ELSE 0 END AS BIT),
                CAST(NULL AS NVARCHAR(10)), CAST(NULL AS NVARCHAR(40)),
                CAST(NULL AS NVARCHAR(60)), [balance],
                [statementDate], [owner], [importedAt], [status], [statementFileId]
        FROM    [SantanderTransactions]
        WHERE   [statementFileId] = @Id
        UNION ALL
        SELECT  [transactionId], [date], CAST(NULL AS NVARCHAR(20)), [description],
                [amount], [isCredit],
                CAST(NULL AS NVARCHAR(10)), CAST(NULL AS NVARCHAR(40)),
                CAST(NULL AS NVARCHAR(60)), CAST(NULL AS NVARCHAR(40)),
                [statementDate], [owner], [importedAt], [status], [statementFileId]
        FROM    [ChaseTransactions]
        WHERE   [statementFileId] = @Id
        UNION ALL
        SELECT  [transactionId], [date], CAST(NULL AS NVARCHAR(20)), [description],
                [amount], [isCredit],
                CAST(NULL AS NVARCHAR(10)), CAST(NULL AS NVARCHAR(40)),
                [type], [balance],
                [statementDate], [owner], [importedAt], [status], [statementFileId]
        FROM    [SofiTransactions]
        WHERE   [statementFileId] = @Id
        ORDER BY [transactionDate] ASC, [description] ASC;
        """;

    public async Task<Result<GetStatementResponse>> ExecuteAsync(GetStatementRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(Sql, new { request.Id }, cancellationToken: ct));

        var statement = await grid.ReadSingleOrDefaultAsync<GetStatementResponse>();
        var rows = await grid.ReadAsync<StagedStatementRow>();

        if (statement is null) return Result<GetStatementResponse>.NotFound("Statement not found");

        // stagedRows is the length of what came back, not a second COUNT: they
        // would be two answers to the same question, taken a moment apart.
        statement.Rows = rows.AsList();
        statement.StagedRows = statement.Rows.Count;

        return Result<GetStatementResponse>.Success(statement);
    }
}
