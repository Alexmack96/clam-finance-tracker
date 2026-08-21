using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Users.ProvisionCurrentUser;

/// POST users/me, not POST admin/users. It creates exactly one row, the
/// caller's, and the caller's identity comes from their token rather than the
/// body, so there is no version of this that creates somebody else.
public sealed class ProvisionCurrentUserEndpoint(ProvisionCurrentUserCommand command)
    : ResultEndpoint<ProvisionCurrentUserRequest, UserSummary>
{
    public override void Configure()
    {
        Post("users/me");
        Description(b => b.WithName("ProvisionCurrentUser"));
    }

    public override async Task HandleAsync(ProvisionCurrentUserRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
