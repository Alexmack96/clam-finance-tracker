namespace Clam.Api.Features.Import.ProcessStaged;

/// Amex prints a currency's *name* under a foreign row — "MALAYSIAN RINGGIT" —
/// while <c>Transactions.originalCurrency</c> holds an ISO code, because that is
/// what the USD banks already write there.
///
/// A lookup rather than a library: the set of currencies that actually appear on
/// these statements is small and closed, and a name that is not in it should
/// produce nothing rather than a guess. An unrecognised name is not data loss —
/// the staged row keeps the statement's own wording either way — so the safe
/// behaviour is to leave the normalised row saying only "this was sterling
/// converted from something", and to widen this table when a new currency turns
/// up in the staging data.
internal static class CurrencyNames
{
    private static readonly Dictionary<string, string> IsoByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UNITED STATES DOLLAR"] = "USD",
        ["EURO"] = "EUR",
        ["JAPANESE YEN"] = "JPY",
        ["SWISS FRANC"] = "CHF",
        ["CANADIAN DOLLAR"] = "CAD",
        ["AUSTRALIAN DOLLAR"] = "AUD",
        ["NEW ZEALAND DOLLAR"] = "NZD",
        ["MALAYSIAN RINGGIT"] = "MYR",
        ["POLISH ZLOTY"] = "PLN",
        ["SINGAPORE DOLLAR"] = "SGD",
        ["HONG KONG DOLLAR"] = "HKD",
        ["THAI BAHT"] = "THB",
        ["INDIAN RUPEE"] = "INR",
        ["SOUTH AFRICAN RAND"] = "ZAR",
        ["NORWEGIAN KRONE"] = "NOK",
        ["SWEDISH KRONA"] = "SEK",
        ["DANISH KRONE"] = "DKK",
        ["CZECH KORUNA"] = "CZK",
        ["HUNGARIAN FORINT"] = "HUF",
        ["TURKISH LIRA"] = "TRY",
        ["MEXICAN PESO"] = "MXN",
        ["UNITED ARAB EMIRATES DIRHAM"] = "AED",
    };

    internal static string? ToIsoCode(string? printedName) =>
        printedName is not null && IsoByName.TryGetValue(printedName.Trim(), out var iso) ? iso : null;
}
