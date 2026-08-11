using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Monzo;
using Dapper;

namespace Clam.Api.Features.Monzo;

/// The stored OAuth credential.
public sealed class MonzoCredential
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }

    /// The retail account, resolved on first sync. Used only for display and to
    /// tell the Flex card apart from the current account at process time.
    public string? AccountId { get; set; }
}

/// Resolving a usable credential and the accounts to sync against.
/// Feature-shared (tier 2): sync and the manual rec both need exactly this, and
/// the token refresh in particular must not exist twice.
public sealed class MonzoConnectionResolver(IDbConnectionFactory factory, IMonzoApiClient api, TimeProvider clock)
{
    /// Refresh this far ahead of expiry, so a long sync cannot have its token
    /// die underneath it mid-run.
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);

    /// Account types worth pulling transactions from. `uk_monzo_flex` is
    /// undocumented but real; its `backing_loan` sibling returns 403 on every
    /// endpoint and is deliberately excluded.
    private static readonly HashSet<string> SyncedAccountTypes =
        new(StringComparer.Ordinal) { "uk_retail", "uk_monzo_flex" };

    private const string SelectSql = """
        SELECT TOP 1 [id], [userId], [accessToken], [refreshToken], [expiresAt], [accountId]
        FROM [MonzoCredentials];
        """;

    private const string UpdateTokensSql = """
        UPDATE  [MonzoCredentials]
        SET     [accessToken] = @AccessToken, [refreshToken] = @RefreshToken,
                [expiresAt] = @ExpiresAt, [updatedAt] = SYSUTCDATETIME()
        WHERE   [id] = @Id;
        """;

    private const string SetAccountIdSql = """
        UPDATE [MonzoCredentials] SET [accountId] = @AccountId, [updatedAt] = SYSUTCDATETIME() WHERE [id] = @Id;
        """;

    private const string DeleteSql = "DELETE FROM [MonzoCredentials] WHERE [id] = @Id;";

    public sealed record Connection(MonzoCredential Credential, IReadOnlyList<MonzoAccount> Accounts);

    public async Task<Result<Connection>> ResolveAsync(CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);

        var credential = await connection.QuerySingleOrDefaultAsync<MonzoCredential>(
            new CommandDefinition(SelectSql, cancellationToken: ct));

        if (credential is null)
            return Result<Connection>.Invalid(new ValidationError("Monzo not connected — click Connect Monzo first"));

        if (credential.ExpiresAt - clock.GetUtcNow().UtcDateTime <= RefreshBuffer)
        {
            MonzoTokens tokens;
            try
            {
                tokens = await api.RefreshAsync(credential.RefreshToken, ct);
            }
            catch (MonzoApiException ex)
            {
                // The refresh token is dead, so the credential is worthless —
                // deleting it is what makes the UI offer "Connect" again rather
                // than a sync button that can only fail.
                await connection.ExecuteAsync(new CommandDefinition(
                    DeleteSql, new { credential.Id }, cancellationToken: ct));

                return Result<Connection>.Unauthorized(
                    $"Monzo token expired and could not be refreshed — please reconnect: {ex.Message}");
            }

            credential.AccessToken = tokens.AccessToken;
            credential.RefreshToken = tokens.RefreshToken;
            credential.ExpiresAt = tokens.ExpiresAt;

            await connection.ExecuteAsync(new CommandDefinition(UpdateTokensSql, new
            {
                credential.Id,
                credential.AccessToken,
                credential.RefreshToken,
                credential.ExpiresAt,
            }, cancellationToken: ct));
        }

        IReadOnlyList<MonzoAccount> accounts;
        try
        {
            accounts = await api.GetAccountsAsync(credential.AccessToken, ct);
        }
        catch (MonzoApiException ex)
        {
            return Result<Connection>.Error($"Could not fetch Monzo accounts: {ex.Message}");
        }

        var syncable = accounts.Where(a => !a.Closed && SyncedAccountTypes.Contains(a.Type)).ToList();
        if (syncable.Count == 0)
            return Result<Connection>.Error("No open Monzo retail or Flex account found");

        if (string.IsNullOrEmpty(credential.AccountId))
        {
            var retail = syncable.Find(a => string.Equals(a.Type, "uk_retail", StringComparison.Ordinal)) ?? syncable[0];
            credential.AccountId = retail.Id;

            await connection.ExecuteAsync(new CommandDefinition(
                SetAccountIdSql, new { credential.Id, credential.AccountId }, cancellationToken: ct));
        }

        return Result<Connection>.Success(new Connection(credential, syncable));
    }
}
