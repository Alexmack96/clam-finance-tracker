namespace Clam.Api.Features.Users.GetCurrentUser;

/// Wrapped in a `user` object rather than returned bare — the Express route
/// answers `{ user: { id, email, name } }` and the client destructures it.
public sealed class GetCurrentUserResponse
{
    public CurrentUser User { get; set; } = new();
}

public sealed class CurrentUser
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
}
