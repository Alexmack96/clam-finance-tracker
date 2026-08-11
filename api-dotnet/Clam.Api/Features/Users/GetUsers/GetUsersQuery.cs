using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Users.GetUsers;

public sealed class GetUsersQuery(IDbConnectionFactory factory)
{
    private const string Sql = """
        SELECT  [id], [name], [email], [createdAt]
        FROM    [Users]
        ORDER BY [createdAt] DESC;
        """;

    public async Task<IReadOnlyList<UserSummary>> ExecuteAsync(CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<UserSummary>(new CommandDefinition(Sql, cancellationToken: ct));
        return rows.AsList();
    }
}
