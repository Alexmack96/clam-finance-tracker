using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Notes.DeleteNote;

public sealed class DeleteNoteCommand(IDbConnectionFactory factory)
{
    private const string Sql = "DELETE FROM [Notes] WHERE [id] = @Id;";

    public async Task<Result> ExecuteAsync(DeleteNoteRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(Sql, new { request.Id }, cancellationToken: ct));

        return affected == 0 ? Result.NotFound("Note not found") : Result.NoContent();
    }
}
