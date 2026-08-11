using System.Security.Cryptography;
using Clam.Api.Infrastructure.Auth;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Monzo;
using Dapper;
using FastEndpoints;

namespace Clam.Api.Features.Monzo.StartMonzoAuth;

/// Redirects the browser to Monzo's consent screen. It answers with a 302 rather
/// than JSON, so it handles its own response — there is nothing here for a
/// command to return.
public sealed class StartMonzoAuthEndpoint(
    IDbConnectionFactory factory,
    ISessionReader sessions,
    MonzoOptions options,
    TimeProvider clock) : EndpointWithoutRequest
{
    /// Borrowed from Better Auth's generic key/value store. A dedicated table
    /// for one ten-minute value would be a migration for nothing.
    internal const string StateIdentifier = "monzo-oauth-state";

    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private const string Sql = """
        INSERT INTO [Verifications] ([id], [identifier], [value], [expiresAt])
        VALUES (@Id, @Identifier, @Value, @ExpiresAt);
        """;

    public override void Configure()
    {
        Get("admin/monzo/auth");
        AllowAnonymous();
        Description(b => b.WithName("StartMonzoAuth"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!options.IsConfigured)
        {
            await Send.StringAsync(
                "Monzo OAuth not configured — set Monzo:ClientId, Monzo:ClientSecret and Monzo:RedirectUri",
                StatusCodes.Status503ServiceUnavailable,
                cancellation: ct);
            return;
        }

        // The credential is stored against the user who authorised it, so the
        // callback needs to know who started the flow — and the callback itself
        // cannot be authenticated, because Monzo is what calls it.
        var user = await sessions.GetCurrentUserAsync(HttpContext, ct);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var state = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

        using var connection = await factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(Sql, new
        {
            Id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)),
            Identifier = StateIdentifier,
            Value = MonzoOAuthState.Serialise(state, user.Id),
            ExpiresAt = clock.GetUtcNow().UtcDateTime.Add(StateLifetime),
        }, cancellationToken: ct));

        var url = $"https://auth.monzo.com/?client_id={Uri.EscapeDataString(options.ClientId!)}"
            + $"&redirect_uri={Uri.EscapeDataString(options.RedirectUri!)}"
            + $"&response_type=code&state={Uri.EscapeDataString(state)}";

        await Send.RedirectAsync(url, isPermanent: false, allowRemoteRedirects: true);
    }
}
