namespace Clam.Api.Features.Monzo;

/// The one message every slice in this feature gives when the OAuth app was
/// never configured. Feature-shared so the four places that say it cannot drift
/// into naming three different settings.
internal static class MonzoOptionsMessages
{
    internal const string NotConfigured =
        "Monzo OAuth not configured — set Monzo:ClientId, Monzo:ClientSecret and Monzo:RedirectUri";
}
