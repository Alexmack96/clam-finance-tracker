using Clam.Api.Features.Users;

namespace Clam.Api.Features.Users.GetUsers;

/// A bare JSON array, matching `res.json(users)`.
public sealed class GetUsersResponse : List<UserSummary>
{
    public GetUsersResponse(IEnumerable<UserSummary> users) : base(users) { }
}
