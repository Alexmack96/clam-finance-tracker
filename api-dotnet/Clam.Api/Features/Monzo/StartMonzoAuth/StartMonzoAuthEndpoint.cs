using System.Security.Cryptography;
using Clam.Api.Infrastructure.Auth;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Monzo;
using Dapper;
using FastEndpoints;

namespace Clam.Api.Features.Monzo.StartMonzoAuth;

public sealed class StartMonzoAuthResponse
{
    public string Url { get; set; } = "";
}

/// Hands back Monzo's consent URL for the client to navigate to.
///
/// It used to answer 302 and the Import page linked straight at it. That worked
/// while a session cookie carried the caller's identity, because the browser
/// attaches cookies to a plain link. A bearer token it does not attach, so a
/// link would arrive here unauthenticated every time. Returning the URL lets the
/// client send its token on an ordinary XHR and then set window.location itself.
public sealed class StartMonzoAuthEndpoint(
    IDbConnectionFactory factory,
    ICurrentUserAccessor currentUser,
    MonzoOptions options,
    TimeProvider clock) : EndpointWithoutRequest<StartMonzoAuthResponse>
{
    /// Better Auth is gone but its generic key/value table stays, and this still
    /// borrows it. A dedicated table for one ten-minute value would be a
    /// migration for nothing.
    internal const string StateIdentifier = "monzo-oauth-state";

    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private const string Sql = """
        INSERT INTO [Verifications] ([id], [identifier], [value], [expiresAt])
        VALUES (@Id, @Identifier, @Value, @ExpiresAt);
        """;

    public override void Configure()
    {
        Get("admin/monzo/auth");
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
        // callback needs to know who started the flow. The callback cannot ask,
        // because Monzo is what calls it, so the answer rides in the state value.
        var caller = currentUser.Get();
        if (caller?.UserId is null)
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
            Value = MonzoOAuthState.Serialise(state, caller.UserId),
            ExpiresAt = clock.GetUtcNow().UtcDateTime.Add(StateLifetime),
        }, cancellationToken: ct));

        var url = $"https://auth.monzo.com/?client_id={Uri.EscapeDataString(options.ClientId!)}"
            + $"&redirect_uri={Uri.EscapeDataString(options.RedirectUri!)}"
            + $"&response_type=code&state={Uri.EscapeDataString(state)}";

        await Send.OkAsync(new StartMonzoAuthResponse { Url = url }, ct);
    }
}
