using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Infrastructure.Auth;

/// The signed-in user, as the Express `requireAuth` middleware resolves them.
public sealed record SessionUser(string Id, string Email, string Name);

public interface ISessionReader
{
    /// Resolves the Better Auth session cookie on the current request, or null
    /// when there is no valid one.
    Task<SessionUser?> GetCurrentUserAsync(HttpContext context, CancellationToken ct = default);
}

/// Reads Better Auth's server-side session directly out of the database.
///
/// This service does not issue sessions, rotate them or verify the cookie's HMAC
/// signature — Better Auth on the Express side owns all of that. What it does is
/// the one thing an endpoint here needs: turn the cookie into a user. The token
/// is a 32-byte random value looked up by equality, so a forged signature buys an
/// attacker nothing without the token itself.
public sealed class DatabaseSessionReader(IDbConnectionFactory factory) : ISessionReader
{
    /// Better Auth's cookie name. The `__Secure-` prefix is what it uses when
    /// running over HTTPS, so both spellings have to be tried.
    private const string CookieName = "better-auth.session_token";
    private const string SecureCookieName = "__Secure-better-auth.session_token";

    private const string Sql = """
        SELECT  u.[id], u.[email], u.[name]
        FROM    [Sessions] s
        JOIN    [Users] u ON u.[id] = s.[userId]
        WHERE   s.[token] = @Token
          AND   s.[expiresAt] > SYSUTCDATETIME()
        """;

    public async Task<SessionUser?> GetCurrentUserAsync(HttpContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var raw = context.Request.Cookies[CookieName] ?? context.Request.Cookies[SecureCookieName];
        if (string.IsNullOrEmpty(raw)) return null;

        // The cookie is `<token>.<hmac>`. The token itself never contains a dot,
        // so the first segment is the whole of it.
        var token = raw.Split('.', 2)[0];
        if (token.Length == 0) return null;

        using var connection = await factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<SessionUser>(
            new CommandDefinition(Sql, new { Token = token }, cancellationToken: ct));
    }
}
