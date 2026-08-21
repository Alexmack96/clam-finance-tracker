namespace Clam.Api.Infrastructure.Security;

/// WorkOS AuthKit coordinates for one environment, all derived from the client
/// id. They match the discovery document at
/// <c>{Issuer}/.well-known/openid-configuration</c>, which is the source of
/// truth — check it there before editing anything here.
///
/// Ported from PremPoints, with one difference: a blank client id is a value
/// here rather than an exception. PremPoints throws, because it has exactly one
/// way to run. This API has two — configured, and the local/test host that boots
/// without credentials — so the decision about what a blank id *means* belongs
/// to the caller. See <see cref="SecurityExtensions.AddWorkOsAuthentication"/>,
/// which fails fast in Production and leaves the API open everywhere else.
public sealed class WorkOsOptions
{
    public const string SectionName = "WorkOS";

    private WorkOsOptions(string clientId, string? authority, string? audience)
    {
        ClientId = clientId;
        Issuer = authority is { Length: > 0 }
            ? authority
            : $"https://api.workos.com/user_management/{clientId}";
        Audience = audience is { Length: > 0 } ? audience : null;
    }

    public string ClientId { get; }

    /// Token issuer, and the JWT bearer Authority. WorkOS mints access tokens
    /// with this as <c>iss</c> and publishes its signing keys under it.
    ///
    /// Overridable for an AuthKit custom domain (https://sub.authkit.app), whose
    /// tokens carry that as the issuer instead.
    public string Issuer { get; }

    /// Null for the classic User Management issuer, which omits <c>aud</c>
    /// entirely — validating it there rejects every token. AuthKit custom
    /// domains do set it to the client id.
    public string? Audience { get; }

    /// Where the browser is sent to sign in. Used by the Swagger UI flow; the
    /// React client's AuthKit SDK builds its own.
    public static Uri AuthorizationUrl { get; } = new("https://api.workos.com/user_management/authorize");

    /// Where an authorization code is exchanged for an access token. WorkOS
    /// calls this endpoint "authenticate" rather than "token", but it takes the
    /// standard form-encoded <c>grant_type=authorization_code</c> body.
    public static Uri TokenUrl { get; } = new("https://api.workos.com/user_management/authenticate");

    /// Null when no client id is configured.
    public static WorkOsOptions? TryFromConfiguration(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var clientId = config[$"{SectionName}:ClientId"];

        return string.IsNullOrWhiteSpace(clientId)
            ? null
            : new WorkOsOptions(
                clientId,
                config[$"{SectionName}:Authority"],
                config[$"{SectionName}:Audience"]);
    }
}
