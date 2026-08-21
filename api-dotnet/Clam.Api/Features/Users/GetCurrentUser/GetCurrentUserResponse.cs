namespace Clam.Api.Features.Users.GetCurrentUser;

/// Wrapped in a `user` object rather than returned bare. The Express route
/// answered `{ user: { id, email, name } }` and the client destructures it.
///
/// `owner` is new, and it is why this endpoint matters beyond a login check:
/// it says which of Alex and Casey is signed in, and four pages default their
/// figures to that. Nothing in a WorkOS token can tell the client that.
public sealed class GetCurrentUserResponse
{
    public CurrentUserDto User { get; set; } = new();
}

public sealed class CurrentUserDto
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Owner { get; set; }
}
