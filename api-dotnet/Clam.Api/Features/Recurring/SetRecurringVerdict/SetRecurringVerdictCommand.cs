using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Recurring.SetRecurringVerdict;

public sealed class SetRecurringVerdictCommand(IDbConnectionFactory factory, IIdGenerator ids)
{
    private const string DeleteSql = """
        DELETE FROM [RecurringVerdicts] WHERE [owner] = @Owner AND [description] = @Description;
        """;

    /// MERGE, because identity is (owner, description) and the client sends the
    /// same body whether or not a verdict already exists. The unique constraint
    /// on that pair is what makes the match deterministic.
    private const string UpsertSql = """
        MERGE   [RecurringVerdicts] WITH (HOLDLOCK) AS target
        USING   (SELECT @Owner AS [owner], @Description AS [description]) AS source
        ON      target.[owner] = source.[owner] AND target.[description] = source.[description]
        WHEN MATCHED THEN
            UPDATE SET [status] = @Status, [updatedAt] = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN
            INSERT ([id], [owner], [description], [status])
            VALUES (@Id, @Owner, @Description, @Status)
        OUTPUT  INSERTED.[id], INSERTED.[owner], INSERTED.[description],
                INSERTED.[status], INSERTED.[note], INSERTED.[createdAt], INSERTED.[updatedAt];
        """;

    public async Task<Result<RecurringVerdictRecord>> ExecuteAsync(
        SetRecurringVerdictRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owner = request.Owner.ToString();

        using var connection = await factory.OpenAsync(ct);

        if (request.Status is null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                DeleteSql, new { Owner = owner, request.Description }, cancellationToken: ct));
            return Result<RecurringVerdictRecord>.NoContent();
        }

        var parameters = new
        {
            Id = ids.NewId(),
            Owner = owner,
            request.Description,
            Status = request.Status.Value.ToString(),
        };

        var verdict = await connection.QuerySingleAsync<RecurringVerdictRecord>(
            new CommandDefinition(UpsertSql, parameters, cancellationToken: ct));

        return Result<RecurringVerdictRecord>.Success(verdict);
    }
}
