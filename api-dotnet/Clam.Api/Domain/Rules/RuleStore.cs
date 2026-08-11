using System.Data;
using Dapper;

namespace Clam.Api.Domain.Rules;

/// Loading rules, in one place, for the same reason <see cref="RuleEngine"/>
/// interprets them in one place: the Rules dry-run and the import pipeline both
/// run the saved rule set, and a difference in *which rules got loaded* is
/// indistinguishable from a difference in how they matched.
///
/// Static, and takes the caller's connection rather than a factory, so a slice
/// that is already inside a transaction reads the same snapshot it is writing.
public static class RuleStore
{
    /// Ordered by kind then position because position is the whole of
    /// precedence within a kind; the engine re-checks it, but a caller that
    /// enumerates rules directly gets them in the order they run.
    private const string RulesSql = """
        SELECT  [id], [kind], [position], [joinOperator], [bank], [categoryId], [bucket], [createdAt]
        FROM    [Rules]
        ORDER BY [kind] ASC, [position] ASC;

        SELECT  c.[id], c.[ruleId], c.[field], c.[operator], c.[value], c.[negate], c.[position]
        FROM    [RuleConditions] c
        ORDER BY c.[ruleId] ASC, c.[position] ASC;
        """;

    private const string CategoryNamesSql = "SELECT [id], [name] FROM [Categories];";

    private const string SingleRuleSql = """
        SELECT  [id], [kind], [position], [joinOperator], [bank], [categoryId], [bucket], [createdAt]
        FROM    [Rules]
        WHERE   [id] = @Id;

        SELECT  [id], [ruleId], [field], [operator], [value], [negate], [position]
        FROM    [RuleConditions]
        WHERE   [ruleId] = @Id
        ORDER BY [position] ASC;
        """;

    public static async Task<List<Rule>> LoadRulesAsync(
        IDbConnection connection,
        CancellationToken ct,
        IDbTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Two result sets and one stitch, not a join: a join repeats every rule
        // column once per condition, and rules are read on every import.
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(RulesSql, transaction: transaction, cancellationToken: ct));

        var rules = (await grid.ReadAsync<Rule>()).ToList();
        var conditions = await grid.ReadAsync<ConditionRow>();

        var byRule = rules.ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var row in conditions)
        {
            if (byRule.TryGetValue(row.RuleId, out var rule)) rule.Conditions.Add(row.ToCondition());
        }

        return rules;
    }

    /// One rule with its conditions — what a write slice returns after the
    /// write, so the client sees the row the database actually holds rather than
    /// the one it hoped for.
    public static async Task<Rule?> LoadRuleAsync(
        IDbConnection connection,
        string id,
        IDbTransaction? transaction,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(SingleRuleSql, new { Id = id }, transaction, cancellationToken: ct));

        var rule = await grid.ReadSingleOrDefaultAsync<Rule>();
        var conditions = await grid.ReadAsync<ConditionRow>();

        if (rule is null) return null;

        rule.Conditions.AddRange(conditions.Select(c => c.ToCondition()));
        return rule;
    }

    public static async Task<Dictionary<string, string>> LoadCategoryNamesAsync(
        IDbConnection connection,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var rows = await connection.QueryAsync<(string Id, string Name)>(
            new CommandDefinition(CategoryNamesSql, cancellationToken: ct));

        return rows.ToDictionary(r => r.Id, r => r.Name, StringComparer.Ordinal);
    }

    /// Carries `ruleId` so the stitch above has something to match on. It is not
    /// on <see cref="RuleCondition"/> because the client never sees it — the
    /// condition is always already nested inside its rule.
    private sealed class ConditionRow
    {
        public string Id { get; set; } = "";
        public string RuleId { get; set; } = "";
        public RuleField Field { get; set; }
        public RuleOperator Operator { get; set; }
        public string Value { get; set; } = "";
        public bool Negate { get; set; }
        public int Position { get; set; }

        public RuleCondition ToCondition() => new()
        {
            Id = Id,
            Field = Field,
            Operator = Operator,
            Value = Value,
            Negate = Negate,
            Position = Position,
        };
    }
}
