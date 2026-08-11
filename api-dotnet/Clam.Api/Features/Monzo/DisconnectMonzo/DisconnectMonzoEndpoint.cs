using Clam.Api.Infrastructure.Auth;
using Clam.Api.Infrastructure.Data;
using Dapper;
using FastEndpoints;

namespace Clam.Api.Features.Monzo.DisconnectMonzo;

public sealed class DisconnectMonzoResponse
{
    public bool Ok { get; set; } = true;
}

/// Forgets the stored credential. The staged transactions are deliberately left
/// alone: they are already imported data, and disconnecting a bank is not a
/// request to delete your history.
public sealed class DisconnectMonzoEndpoint(IDbConnectionFactory factory, ISessionReader sessions)
    : EndpointWithoutRequest<DisconnectMonzoResponse>
{
    private const string Sql = "DELETE FROM [MonzoCredentials] WHERE [userId] = @UserId;";

    public override void Configure()
    {
        Post("admin/monzo/disconnect");
        AllowAnonymous();
        Description(b => b.WithName("DisconnectMonzo"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await sessions.GetCurrentUserAsync(HttpContext, ct);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        using var connection = await factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(Sql, new { UserId = user.Id }, cancellationToken: ct));

        // Always ok, even when there was nothing to delete: the caller asked for
        // "not connected", and that is the state either way.
        await Send.OkAsync(new DisconnectMonzoResponse(), ct);
    }
}
