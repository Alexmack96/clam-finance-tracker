namespace Clam.Api.Features.Users;

/// A user as this API ever exposes one. Feature-shared (tier 2) between the list
/// and the create slices.
///
/// Deliberately four fields: `image` and `owner` are unused by any caller, and
/// the credential lives in a separate table that this type must never reach.
public sealed class UserSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
