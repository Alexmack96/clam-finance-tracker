using System.Data;
using Clam.Api.Domain;
using Dapper;

namespace Clam.Api.Features.Rules;

/// Reading the rows a plan runs over, and writing a plan back. Feature-shared
/// (tier 2) because preview, apply and delete all need one or both.
public static class RulePlanStore
{
    private const string PlanTransactionsSql = """
        SELECT  [id], [date], [description], [amount], [type], [externalId],
                [categoryId], [bucket], [categoryPinned], [bucketPinned]
        FROM    [Transactions]
        ORDER BY [date] DESC;
        """;

    private const string ApplyCategorySql = """
        UPDATE  [Transactions]
        SET     [categoryId] = @CategoryId
        WHERE   [id] IN @Ids;
        """;

    private const string ApplyBucketSql = """
        UPDATE  [Transactions]
        SET     [bucket] = @Bucket
        WHERE   [id] IN @Ids;
        """;

    public static async Task<List<PlanTransaction>> LoadPlanTransactionsAsync(
        IDbConnection connection,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var rows = await connection.QueryAsync<PlanTransaction>(
            new CommandDefinition(PlanTransactionsSql, cancellationToken: ct));
        return rows.AsList();
    }

    /// Writes a plan, grouped by target value so an 1800-row run is a handful of
    /// statements rather than a row-at-a-time loop. Never touches the pin flags —
    /// a pin is only ever set by a hand edit.
    public static async Task<(int CategoryChanges, int BucketChanges)> ApplyPlanAsync(
        IDbConnection connection,
        Plan plan,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(plan);

        var byCategory = plan.Rows
            .Where(r => r.NextCategoryId is not null)
            .GroupBy(r => r.NextCategoryId!, StringComparer.Ordinal);

        var byBucket = plan.Rows
            .Where(r => r.NextBucket is not null)
            .GroupBy(r => r.NextBucket!.Value);

        var categoryChanges = 0;
        var bucketChanges = 0;

        using var transaction = connection.BeginTransaction();

        foreach (var group in byCategory)
        {
            foreach (var chunk in group.Select(r => r.Transaction.Id).Chunk(ParameterChunkSize))
            {
                categoryChanges += await connection.ExecuteAsync(new CommandDefinition(
                    ApplyCategorySql, new { CategoryId = group.Key, Ids = chunk }, transaction, cancellationToken: ct));
            }
        }

        foreach (var group in byBucket)
        {
            foreach (var chunk in group.Select(r => r.Transaction.Id).Chunk(ParameterChunkSize))
            {
                bucketChanges += await connection.ExecuteAsync(new CommandDefinition(
                    ApplyBucketSql, new { Bucket = group.Key.ToString(), Ids = chunk }, transaction, cancellationToken: ct));
            }
        }

        transaction.Commit();
        return (categoryChanges, bucketChanges);
    }

    /// Dapper expands `IN @Ids` into one parameter per id, and SQL Server caps a
    /// batch at 2100 parameters. Chunking is what stops a large run failing at
    /// exactly the point it becomes worth running.
    private const int ParameterChunkSize = 1000;
}
