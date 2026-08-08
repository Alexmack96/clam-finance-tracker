using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Transactions.GetTransactions;

/// The data layer for this slice and nothing else. It is a class rather than a
/// static helper so the endpoint takes it as a constructor dependency and a test
/// can substitute it.
public sealed class GetTransactionsQuery(IDbConnectionFactory factory)
{
    // `t.*` then `c.*` with splitOn "id": Dapper starts the Category at the second
    // column called `id`, which is exactly where the join's right-hand side begins.
    //
    // Each filter is written as `@p IS NULL OR col = @p` so one cached plan serves
    // every combination. That is the direct translation of the spread-object
    // `where` clause the Express route builds.
    private const string Sql = """
        SELECT  t.[id], t.[description], t.[amount], t.[type], t.[date], t.[createdAt],
                t.[categoryId], t.[externalId], t.[note], t.[owner], t.[reviewed],
                t.[bucket], t.[categoryPinned], t.[bucketPinned], t.[originalAmount],
                t.[originalCurrency], t.[statementFileId],
                c.[id], c.[name], c.[color]
        FROM    [Transaction] t
        JOIN    [Category] c ON c.[id] = t.[categoryId]
        WHERE   (@Type       IS NULL OR t.[type]       = @Type)
          AND   (@CategoryId IS NULL OR t.[categoryId] = @CategoryId)
          AND   (@Owner      IS NULL OR t.[owner]      = @Owner)
        ORDER BY t.[date] DESC
        """;

    public async Task<IReadOnlyList<TransactionRecord>> ExecuteAsync(
        GetTransactionsRequest filter,
        CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);

        var rows = await connection.QueryAsync<TransactionRecord, TransactionCategory, TransactionRecord>(
            new CommandDefinition(Sql, filter, cancellationToken: ct),
            (transaction, category) =>
            {
                transaction.Category = category;
                return transaction;
            },
            splitOn: "id");

        return rows.ToList();
    }
}
