using Ardalis.Result;
using Clam.Api.Domain.Rules;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Rules.ReorderRules;

public sealed class ReorderRulesCommand(IDbConnectionFactory factory)
{
    private const string ExistingIdsSql = "SELECT [id] FROM [Rules] WHERE [kind] = @Kind;";

    private const string SetPositionSql = "UPDATE [Rules] SET [position] = @Position WHERE [id] = @Id;";

    public async Task<Result<ReorderRulesResponse>> ExecuteAsync(
        ReorderRulesRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);
        using var transaction = connection.BeginTransaction();

        var existing = (await connection.QueryAsync<string>(new CommandDefinition(
            ExistingIdsSql, new { Kind = request.Kind.ToString() }, transaction, cancellationToken: ct)))
            .ToHashSet(StringComparer.Ordinal);

        // A partial list would leave the rules it omitted at stale positions,
        // silently changing precedence for rules the user did not touch.
        if (request.Ids.Count != existing.Count || request.Ids.Exists(id => !existing.Contains(id)))
        {
            return Result<ReorderRulesResponse>.Invalid(
                new ValidationError("ids", "Reorder must list every rule of this kind exactly once"));
        }

        var updates = request.Ids.Select((id, i) => new { Id = id, Position = i });
        await connection.ExecuteAsync(new CommandDefinition(
            SetPositionSql, updates, transaction, cancellationToken: ct));

        var rules = await RuleStore.LoadRulesAsync(connection, ct, transaction);
        transaction.Commit();

        var reordered = rules
            .Where(r => r.Kind == request.Kind)
            .OrderBy(r => r.Position);

        return Result<ReorderRulesResponse>.Success(new ReorderRulesResponse(reordered));
    }
}
