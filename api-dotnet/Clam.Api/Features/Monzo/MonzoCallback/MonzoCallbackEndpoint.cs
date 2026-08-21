using Clam.Api.Features.Monzo.StartMonzoAuth;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Monzo;
using Dapper;
using FastEndpoints;

namespace Clam.Api.Features.Monzo.MonzoCallback;

/// Where Monzo sends the browser back to. Deliberately *not* behind
/// authentication: Monzo redirects the user's browser here, and requiring a
/// session would fail whenever the consent screen was opened in a context that
/// does not carry the cookie. The `state` nonce is what makes that safe.
///
/// Every failure ends the same way — a redirect back to the import page with a
/// flag — because the user is looking at a browser, not at a JSON response.
public sealed class MonzoCallbackEndpoint(
    IDbConnectionFactory factory,
    IIdGenerator ids,
    IMonzoApiClient api,
    MonzoOptions options,
    IConfiguration configuration,
    ILogger<MonzoCallbackEndpoint> logger) : Endpoint<MonzoCallbackRequest>
{
    private const string SelectStateSql = """
        SELECT TOP 1 [id], [value]
        FROM   [Verifications]
        WHERE  [identifier] = @Identifier AND [expiresAt] > SYSUTCDATETIME()
        ORDER BY [createdAt] DESC;
        """;

    private const string DeleteStateSql = "DELETE FROM [Verifications] WHERE [id] = @Id;";

    /// The credential is per user and there is at most one, so a re-connect
    /// replaces the tokens and clears the resolved account — it will be resolved
    /// again on the next sync, and the old one may not belong to this login.
    private const string UpsertCredentialSql = """
        MERGE   [MonzoCredentials] WITH (HOLDLOCK) AS target
        USING   (SELECT @UserId AS [userId]) AS source
        ON      target.[userId] = source.[userId]
        WHEN MATCHED THEN
            UPDATE SET [accessToken] = @AccessToken, [refreshToken] = @RefreshToken,
                       [expiresAt] = @ExpiresAt, [accountId] = NULL, [updatedAt] = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN
            INSERT ([id], [userId], [accessToken], [refreshToken], [expiresAt])
            VALUES (@Id, @UserId, @AccessToken, @RefreshToken, @ExpiresAt);
        """;

    public override void Configure()
    {
        Get("admin/monzo/callback");

        // Monzo calls this, not the client, so there is no token to send. What
        // stands in for one is the `state` parameter: single-use, ten-minute,
        // and it names the user who started the flow.
        AllowAnonymous();
        Description(b => b.WithName("MonzoCallback"));
    }

    public override async Task HandleAsync(MonzoCallbackRequest req, CancellationToken ct)
    {
        if (!options.IsConfigured)
        {
            await Send.StringAsync(
                "Monzo OAuth not configured — set Monzo:ClientId, Monzo:ClientSecret and Monzo:RedirectUri",
                StatusCodes.Status503ServiceUnavailable,
                cancellation: ct);
            return;
        }

        if (!string.IsNullOrEmpty(req.Error) || string.IsNullOrEmpty(req.State) || string.IsNullOrEmpty(req.Code))
        {
            logger.LogInformation("Monzo callback missing parameters (error: {Error})", req.Error);
            await FailAsync();
            return;
        }

        using var connection = await factory.OpenAsync(ct);

        var stored = await connection.QuerySingleOrDefaultAsync<StoredState>(
            new CommandDefinition(SelectStateSql, new { Identifier = StartMonzoAuthEndpoint.StateIdentifier },
                cancellationToken: ct));

        if (stored is null)
        {
            logger.LogInformation("Monzo callback found no unexpired state");
            await FailAsync();
            return;
        }

        var payload = MonzoOAuthState.Deserialise(stored.Value);
        if (payload is null || !string.Equals(payload.State, req.State, StringComparison.Ordinal))
        {
            logger.LogWarning("Monzo callback state mismatch");
            await FailAsync();
            return;
        }

        // Consumed whether or not the exchange succeeds: a state is good for one
        // attempt, and leaving it usable is what a replay would need.
        await connection.ExecuteAsync(new CommandDefinition(
            DeleteStateSql, new { stored.Id }, cancellationToken: ct));

        MonzoTokens tokens;
        try
        {
            tokens = await api.ExchangeCodeAsync(req.Code, ct);
        }
        catch (MonzoApiException ex)
        {
            logger.LogWarning("Monzo token exchange failed with {StatusCode}: {Message}", ex.StatusCode, ex.Message);
            await FailAsync();
            return;
        }

        await connection.ExecuteAsync(new CommandDefinition(UpsertCredentialSql, new
        {
            Id = ids.NewId(),
            payload.UserId,
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.ExpiresAt,
        }, cancellationToken: ct));

        await Send.RedirectAsync($"{ClientUrl()}/import?monzo=connected",
            isPermanent: false, allowRemoteRedirects: true);
    }

    private Task FailAsync() =>
        Send.RedirectAsync($"{ClientUrl()}/import?monzo=error", isPermanent: false, allowRemoteRedirects: true);

    /// Falls back to the first allowed CORS origin, which is where the client is
    /// served from — the same default the Express route takes.
    private string ClientUrl() =>
        options.ClientUrl
        ?? configuration.GetSection("AllowedOrigins").Get<string[]>()?.FirstOrDefault()
        ?? "http://localhost:5173";

    private sealed class StoredState
    {
        public string Id { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
