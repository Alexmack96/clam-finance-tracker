using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Notes.CreateNote;

public sealed class CreateNoteCommand(IDbConnectionFactory factory, IIdGenerator ids)
{
    /// OUTPUT rather than a second SELECT: the row the client gets back carries
    /// the database's own createdAt/updatedAt, not a clock reading from here.
    private const string Sql = $"""
        INSERT INTO [Notes] ([id], [title], [body], [pinned])
        OUTPUT {NoteSql.Inserted}
        VALUES (@Id, @Title, @Body, @Pinned);
        """;

    public async Task<NoteRecord> ExecuteAsync(CreateNoteRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = new { Id = ids.NewId(), request.Title, request.Body, request.Pinned };

        using var connection = await factory.OpenAsync(ct);
        return await connection.QuerySingleAsync<NoteRecord>(
            new CommandDefinition(Sql, parameters, cancellationToken: ct));
    }
}
