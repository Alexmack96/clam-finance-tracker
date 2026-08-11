using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Rules.DeleteRule;

public sealed class DeleteRuleCommand(IDbConnectionFactory factory)
{
    private const string KindSql = "SELECT [kind] FROM [Rules] WHERE [id] = @Id;";

    private const string DeleteSql = "DELETE FROM [Rules] WHERE [id] = @Id;";

    /// Close the gap so positions stay dense. Sparse positions still resolve
    /// correctly — precedence is only ever compared, never counted — but they
    /// make the reorder payload confusing to debug.
    private const string CompactSql = """
        WITH [ordered] AS (
            SELECT [position], ROW_NUMBER() OVER (ORDER BY [position] ASC) - 1 AS [dense]
            FROM   [Rules]
            WHERE  [kind] = @Kind
        )
        UPDATE [ordered] SET [position] = [dense];
        """;

    public async Task<Result> ExecuteAsync(DeleteRuleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);

        var kind = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(KindSql, new { request.Id }, cancellationToken: ct));
        if (kind is null) return Result.NotFound("Rule not found");

        using var transaction = connection.BeginTransaction();

        // RuleConditions cascade on the FK, so only the rule row is deleted here.
        await connection.ExecuteAsync(new CommandDefinition(
            DeleteSql, new { request.Id }, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            CompactSql, new { Kind = kind }, transaction, cancellationToken: ct));

        transaction.Commit();
        return Result.NoContent();
    }
}
