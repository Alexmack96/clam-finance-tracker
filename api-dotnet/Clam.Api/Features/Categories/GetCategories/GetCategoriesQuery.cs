using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Categories.GetCategories;

public sealed class GetCategoriesQuery(IDbConnectionFactory factory)
{
    // A LEFT JOIN aggregate is the same thing Prisma's `_count` produces, and it
    // keeps categories with no transactions in the result at 0 rather than
    // dropping them — which an INNER JOIN would do silently.
    private const string Sql = """
        SELECT      c.[id],
                    c.[name],
                    c.[color],
                    COUNT(t.[id]) AS [transactionCount]
        FROM        [Categories] c
        LEFT JOIN   [Transactions] t ON t.[categoryId] = c.[id]
        GROUP BY    c.[id], c.[name], c.[color]
        ORDER BY    c.[name] ASC
        """;

    public async Task<IReadOnlyList<CategoryListItem>> ExecuteAsync(CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<CategoryListItem>(
            new CommandDefinition(Sql, cancellationToken: ct));
        return rows.ToList();
    }
}
