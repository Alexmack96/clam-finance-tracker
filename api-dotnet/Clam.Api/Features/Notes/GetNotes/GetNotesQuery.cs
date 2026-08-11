using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Notes.GetNotes;

public sealed class GetNotesQuery(IDbConnectionFactory factory)
{
    /// Pinned first, then most recently touched — the order the board renders in.
    private const string Sql = $"""
        SELECT {NoteSql.Columns}
        FROM   [Notes]
        ORDER BY [pinned] DESC, [updatedAt] DESC;
        """;

    public async Task<IReadOnlyList<NoteRecord>> ExecuteAsync(CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<NoteRecord>(new CommandDefinition(Sql, cancellationToken: ct));
        return rows.AsList();
    }
}
