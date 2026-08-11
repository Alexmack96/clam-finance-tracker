using System.Text.Json;

namespace Clam.Api.Features.Monzo;

/// The `state` parameter's payload, as stored between /auth and /callback.
///
/// It carries the user id as well as the nonce because the callback cannot be
/// authenticated — Monzo's servers are what call it, with no cookie — so the
/// only way to know whose credential this is, is to have written it down before
/// the redirect.
internal sealed record MonzoOAuthState(string State, string UserId)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    internal static string Serialise(string state, string userId) =>
        JsonSerializer.Serialize(new MonzoOAuthState(state, userId), Options);

    /// Returns null for anything that does not parse. A malformed row is
    /// indistinguishable from a forged one as far as this flow is concerned, and
    /// both end the same way.
    internal static MonzoOAuthState? Deserialise(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<MonzoOAuthState>(value, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
