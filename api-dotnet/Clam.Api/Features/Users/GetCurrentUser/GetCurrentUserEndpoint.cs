using Clam.Api.Infrastructure.Auth;
using Clam.Api.Infrastructure.Data;
using Dapper;
using FastEndpoints;

namespace Clam.Api.Features.Users.GetCurrentUser;

/// Who is calling. Three answers, and the client acts on each differently:
///
///   401  no valid token. Sign in.
///   404  the token is good but no local row is linked to it. Provision one by
///        POSTing to admin/users, then ask again.
///   200  the user.
///
/// The 404 is the interesting one. A WorkOS access token carries `sub` and
/// nothing else about the person, so a user who has just accepted an invitation
/// is genuinely signed in and genuinely unknown here. Answering 200 with a
/// half-empty user would hide that; answering 401 would send them back to a sign-in
/// page they have already used, and they would loop.
public sealed class GetCurrentUserEndpoint(ICurrentUserAccessor currentUser, IDbConnectionFactory factory)
    : EndpointWithoutRequest<GetCurrentUserResponse>
{
    private const string Sql =
        "SELECT [id], [email], [name], [owner] FROM [Users] WHERE [id] = @Id;";

    public override void Configure()
    {
        Get("me");
        Description(b => b.WithName("GetCurrentUser"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var caller = currentUser.Get();
        if (caller is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        if (!caller.IsProvisioned)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        using var connection = await factory.OpenAsync(ct);
        var user = await connection.QuerySingleOrDefaultAsync<CurrentUserDto>(
            new CommandDefinition(Sql, new { Id = caller.UserId }, cancellationToken: ct));

        if (user is null)
        {
            // The row was deleted between token validation and now.
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(new GetCurrentUserResponse { User = user }, ct);
    }
}
