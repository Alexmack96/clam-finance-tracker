using FastEndpoints;

namespace Clam.Api.Features.Users.GetUsers;

public sealed class GetUsersEndpoint(GetUsersQuery query) : EndpointWithoutRequest<GetUsersResponse>
{
    public override void Configure()
    {
        Get("admin/users");
        Description(b => b.WithName("GetUsers"));
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await Send.OkAsync(new GetUsersResponse(await query.ExecuteAsync(ct)), ct);
}
