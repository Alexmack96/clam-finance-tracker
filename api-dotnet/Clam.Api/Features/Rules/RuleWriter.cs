using System.Data;
using Clam.Api.Domain;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Rules;

/// Writing a rule's row and its conditions. Feature-shared (tier 2) because
/// create, update and delete all need at least one of these, and the condition
/// insert in particular is where position is assigned — a detail two slices
/// getting subtly different would reorder someone's conditions.
internal static class RuleWriter
{
    private const string InsertRuleSql = """
        INSERT INTO [Rules] ([id], [kind], [position], [joinOperator], [bank], [categoryId], [bucket])
        VALUES (@Id, @Kind, @Position, @JoinOperator, @Bank, @CategoryId, @Bucket);
        """;

    private const string UpdateRuleSql = """
        UPDATE  [Rules]
        SET     [kind]         = @Kind,
                [joinOperator] = @JoinOperator,
                [bank]         = @Bank,
                [categoryId]   = @CategoryId,
                [bucket]       = @Bucket
        WHERE   [id] = @Id;
        """;

    private const string InsertConditionSql = """
        INSERT INTO [RuleConditions] ([id], [ruleId], [field], [operator], [value], [negate], [position])
        VALUES (@Id, @RuleId, @Field, @Operator, @Value, @Negate, @Position);
        """;

    private const string DeleteConditionsSql = "DELETE FROM [RuleConditions] WHERE [ruleId] = @RuleId;";

    private const string NextPositionSql = """
        SELECT COALESCE(MAX([position]), -1) + 1 FROM [Rules] WHERE [kind] = @Kind;
        """;

    /// New rules go to the bottom of their list: a rule you just wrote must
    /// never silently outrank one you already trust.
    internal static Task<int> NextPositionAsync(
        IDbConnection connection, RuleKind kind, IDbTransaction? transaction, CancellationToken ct) =>
        connection.ExecuteScalarAsync<int>(new CommandDefinition(
            NextPositionSql, new { Kind = kind.ToString() }, transaction, cancellationToken: ct));

    internal static Task<int> InsertAsync(
        IDbConnection connection, string id, RuleInput input, int position,
        IDbTransaction? transaction, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            InsertRuleSql, RuleParameters(id, input, position), transaction, cancellationToken: ct));

    internal static Task<int> UpdateAsync(
        IDbConnection connection, string id, RuleInput input,
        IDbTransaction? transaction, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            UpdateRuleSql, RuleParameters(id, input, position: 0), transaction, cancellationToken: ct));

    internal static Task<int> DeleteConditionsAsync(
        IDbConnection connection, string ruleId, IDbTransaction? transaction, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            DeleteConditionsSql, new { RuleId = ruleId }, transaction, cancellationToken: ct));

    /// Position comes from the index in the submitted list, not from the client:
    /// the order the user dragged them into is the order they arrive in.
    internal static Task<int> InsertConditionsAsync(
        IDbConnection connection, IIdGenerator ids, string ruleId, IReadOnlyList<RuleConditionInput> conditions,
        IDbTransaction? transaction, CancellationToken ct)
    {
        var rows = conditions.Select((c, i) => new
        {
            Id = ids.NewId(),
            RuleId = ruleId,
            Field = c.Field.ToString(),
            Operator = c.Operator.ToString(),
            c.Value,
            c.Negate,
            Position = i,
        });

        return connection.ExecuteAsync(new CommandDefinition(
            InsertConditionSql, rows, transaction, cancellationToken: ct));
    }

    /// The kind decides which output column is written and which is nulled. A
    /// Bucket rule carrying a leftover categoryId would assign a category the
    /// user cannot see in the UI.
    private static object RuleParameters(string id, RuleInput input, int position) => new
    {
        Id = id,
        Kind = input.Kind.ToString(),
        Position = position,
        JoinOperator = input.JoinOperator.ToString(),
        input.Bank,
        CategoryId = input.Kind == RuleKind.Category ? input.CategoryId : null,
        Bucket = input.Kind == RuleKind.Bucket ? input.Bucket?.ToString() : null,
    };
}
