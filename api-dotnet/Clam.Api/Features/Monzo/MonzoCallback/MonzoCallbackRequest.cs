namespace Clam.Api.Features.Monzo.MonzoCallback;

/// Bound from the query string Monzo appends to the redirect. All three are
/// optional because a denied consent arrives as `?error=...` with neither of the
/// others.
public sealed class MonzoCallbackRequest
{
    public string? Code { get; set; }
    public string? State { get; set; }
    public string? Error { get; set; }
}
