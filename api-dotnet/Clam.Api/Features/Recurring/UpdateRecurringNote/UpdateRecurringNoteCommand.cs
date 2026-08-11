using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Recurring.UpdateRecurringNote;

public sealed class UpdateRecurringNoteCommand(IDbConnectionFactory factory)
{
    private const string Sql = """
        UPDATE  [RecurringVerdicts]
        SET     [note] = @Note, [updatedAt] = SYSUTCDATETIME()
        OUTPUT  INSERTED.[id], INSERTED.[owner], INSERTED.[description],
                INSERTED.[status], INSERTED.[note], INSERTED.[createdAt], INSERTED.[updatedAt]
        WHERE   [owner] = @Owner AND [description] = @Description;
        """;

    public async Task<Result<RecurringVerdictRecord>> ExecuteAsync(
        UpdateRecurringNoteRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);

        var parameters = new { Owner = request.Owner.ToString(), request.Description, request.Note };
        var updated = await connection.QuerySingleOrDefaultAsync<RecurringVerdictRecord>(
            new CommandDefinition(Sql, parameters, cancellationToken: ct));

        // A note hangs off a verdict, so there has to be one to hang it on.
        return updated is null
            ? Result<RecurringVerdictRecord>.NotFound("Confirm or reject this series before adding a note")
            : Result<RecurringVerdictRecord>.Success(updated);
    }
}
