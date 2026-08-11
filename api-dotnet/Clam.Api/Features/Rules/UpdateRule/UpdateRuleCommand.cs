using Ardalis.Result;
using Clam.Api.Domain.Rules;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Rules.UpdateRule;

public sealed class UpdateRuleCommand(IDbConnectionFactory factory, IIdGenerator ids)
{
    private const string ExistsSql = "SELECT 1 FROM [Rules] WHERE [id] = @Id;";

    public async Task<Result<Rule>> ExecuteAsync(UpdateRuleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);

        var exists = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(ExistsSql, new { request.Id }, cancellationToken: ct));
        if (exists is null) return Result<Rule>.NotFound("Rule not found");

        using var transaction = connection.BeginTransaction();

        // Conditions are replaced wholesale rather than diffed. Position is left
        // untouched: an edit must not move a rule up or down the list.
        await RuleWriter.DeleteConditionsAsync(connection, request.Id, transaction, ct);
        await RuleWriter.UpdateAsync(connection, request.Id, request, transaction, ct);
        await RuleWriter.InsertConditionsAsync(connection, ids, request.Id, request.Conditions, transaction, ct);

        var updated = await RuleStore.LoadRuleAsync(connection, request.Id, transaction, ct);
        transaction.Commit();

        return Result<Rule>.Success(updated!);
    }
}
