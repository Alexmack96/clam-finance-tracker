using System.Security.Claims;
using Clam.Api.Infrastructure.Security;

namespace Clam.Api.Infrastructure.Auth;

/// The caller, as far as this API is concerned.
///
/// <param name="WorkOsUserId">The token's `sub`. Present whenever a token validated.</param>
/// <param name="UserId">
/// The local <c>Users</c> row. Null for a signed-in user who has no row yet —
/// which is a real state, not an error: WorkOS lets someone in the moment they
/// accept an invitation, and the row is created afterwards by the client.
/// </param>
/// <param name="Owner">Alex, Casey or Joint. Null until the row says.</param>
public sealed record CurrentUser(string WorkOsUserId, string? UserId, string? Owner)
{
    public bool IsProvisioned => UserId is not null;
}

public interface ICurrentUserAccessor
{
    /// The caller, or null when the request carries no valid token.
    ///
    /// Reads claims only. The database lookup happened once during token
    /// validation, so this costs nothing per call and every endpoint can ask
    /// freely.
    CurrentUser? Get();
}

public sealed class HttpContextCurrentUserAccessor(IHttpContextAccessor accessor) : ICurrentUserAccessor
{
    public CurrentUser? Get()
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true) return null;

        var workOsUserId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        return workOsUserId is null
            ? null
            : new CurrentUser(
                workOsUserId,
                principal.FindFirst(ClamClaims.UserId)?.Value,
                principal.FindFirst(ClamClaims.Owner)?.Value);
    }
}
