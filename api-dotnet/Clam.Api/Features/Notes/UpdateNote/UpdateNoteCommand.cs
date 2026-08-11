using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Notes.UpdateNote;

public sealed class UpdateNoteCommand(IDbConnectionFactory factory)
{
    private const string Sql = $"""
        UPDATE  [Notes]
        SET     [title]     = COALESCE(@Title, [title]),
                [body]      = COALESCE(@Body, [body]),
                [pinned]    = COALESCE(@Pinned, [pinned]),
                [updatedAt] = SYSUTCDATETIME()
        OUTPUT  {NoteSql.Inserted}
        WHERE   [id] = @Id;
        """;

    public async Task<Result<NoteRecord>> ExecuteAsync(UpdateNoteRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);
        var updated = await connection.QuerySingleOrDefaultAsync<NoteRecord>(
            new CommandDefinition(Sql, request, cancellationToken: ct));

        return updated is null ? Result<NoteRecord>.NotFound("Note not found") : Result<NoteRecord>.Success(updated);
    }
}
