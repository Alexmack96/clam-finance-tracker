namespace Clam.Api.Infrastructure.Monzo;

/// Bound from the `Monzo` configuration section. The secret belongs in
/// user-secrets or the environment, never in appsettings.
public sealed class MonzoOptions
{
    public const string SectionName = "Monzo";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RedirectUri { get; set; }

    /// Where the OAuth callback sends the browser back to. The first entry of
    /// AllowedOrigins when unset, which is where the client is served from.
    public string? ClientUrl { get; set; }

    /// Every endpoint in the feature answers 503 rather than failing obscurely
    /// when the connection was never configured.
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RedirectUri);
}
