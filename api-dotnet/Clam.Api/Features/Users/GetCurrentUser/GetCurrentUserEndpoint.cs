using Clam.Api.Infrastructure.Auth;
using FastEndpoints;

namespace Clam.Api.Features.Users.GetCurrentUser;

/// The one endpoint whose whole job is the session, so it reads the cookie
/// itself rather than going through a query. Everything else here is
/// AllowAnonymous while WorkOS is inactive; this route answers 401 regardless,
/// because "who am I" has no meaningful anonymous answer.
public sealed class GetCurrentUserEndpoint(ISessionReader sessions)
    : EndpointWithoutRequest<GetCurrentUserResponse>
{
    public override void Configure()
    {
        Get("me");
        AllowAnonymous();
        Description(b => b.WithName("GetCurrentUser"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await sessions.GetCurrentUserAsync(HttpContext, ct);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        await Send.OkAsync(new GetCurrentUserResponse
        {
            User = new CurrentUser { Id = user.Id, Email = user.Email, Name = user.Name },
        }, ct);
    }
}
