using Ardalis.Result;
using Clam.Api.Domain.Rules;
using Clam.Api.Infrastructure.Data;
using Microsoft.Data.SqlClient;

namespace Clam.Api.Features.Rules.CreateRule;

public sealed class CreateRuleCommand(IDbConnectionFactory factory, IIdGenerator ids)
{
    private const int ForeignKeyViolation = 547;

    public async Task<Result<Rule>> ExecuteAsync(CreateRuleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = ids.NewId();

        using var connection = await factory.OpenAsync(ct);
        using var transaction = connection.BeginTransaction();

        // Position is read inside the transaction: two rules created at once
        // must not both land on the same position, which is the whole of
        // precedence.
        var position = await RuleWriter.NextPositionAsync(connection, request.Kind, transaction, ct);

        try
        {
            await RuleWriter.InsertAsync(connection, id, request, position, transaction, ct);
        }
        catch (SqlException ex) when (ex.Number == ForeignKeyViolation)
        {
            return Result<Rule>.NotFound("Category not found");
        }

        await RuleWriter.InsertConditionsAsync(connection, ids, id, request.Conditions, transaction, ct);

        var created = await RuleStore.LoadRuleAsync(connection, id, transaction, ct);
        transaction.Commit();

        return Result<Rule>.Created(created!);
    }
}
