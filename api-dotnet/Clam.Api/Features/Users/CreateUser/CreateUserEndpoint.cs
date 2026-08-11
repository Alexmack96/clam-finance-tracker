using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Users.CreateUser;

public sealed class CreateUserEndpoint(CreateUserCommand command) : ResultEndpoint<CreateUserRequest, UserSummary>
{
    public override void Configure()
    {
        Post("admin/users");
        AllowAnonymous();
        Description(b => b.WithName("CreateUser"));
    }

    public override async Task HandleAsync(CreateUserRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
