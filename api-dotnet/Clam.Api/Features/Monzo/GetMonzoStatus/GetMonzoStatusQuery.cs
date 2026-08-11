using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Monzo;
using Dapper;

namespace Clam.Api.Features.Monzo.GetMonzoStatus;

public sealed class GetMonzoStatusQuery(IDbConnectionFactory factory, MonzoOptions options)
{
    private const string Sql = """
        SELECT TOP 1 [accountId] FROM [MonzoCredentials];

        SELECT MAX([created]) FROM [MonzoApiTransactions];

        SELECT COUNT(*) FROM [MonzoApiTransactions];
        """;

    public async Task<GetMonzoStatusResponse> ExecuteAsync(CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(Sql, cancellationToken: ct));

        // Read as a row count rather than the value, because a credential whose
        // accountId is still null is connected but not yet synced.
        var accountIds = (await grid.ReadAsync<string?>()).AsList();
        var lastSyncedAt = await grid.ReadSingleAsync<DateTime?>();
        var totalStaged = await grid.ReadSingleAsync<int>();

        return new GetMonzoStatusResponse
        {
            Configured = options.IsConfigured,
            Connected = accountIds.Count > 0,
            AccountId = accountIds.Count > 0 ? accountIds[0] : null,
            LastSyncedAt = lastSyncedAt,
            TotalStaged = totalStaged,
        };
    }
}
