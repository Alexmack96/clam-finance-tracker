using System.Text.RegularExpressions;

namespace Clam.Api.Features.Import;

/// Owner detection for Monzo transfers and income, so joint-account money
/// movements land on whichever person the description names.
///
/// Only Monzo needs this. Every other bank's statement belongs to one person
/// already, and that owner is stamped on the staged row at upload. This is also
/// independent of category assignment, which is rules-only.
internal static partial class ImportOwner
{
    /// The only Monzo categories where a person's name in the description means
    /// "this is their money". A supermarket run by someone called Alex is not.
    private static readonly HashSet<string> NetCategories =
        new(StringComparer.Ordinal) { "income", "transfers", "finances" };

    internal static string Resolve(string monzoCategory, string merchantName, string defaultOwner)
    {
        if (!NetCategories.Contains(monzoCategory)) return defaultOwner;
        if (AlexPattern().IsMatch(merchantName)) return "Alex";
        if (CaseyPattern().IsMatch(merchantName)) return "Casey";
        return defaultOwner;
    }

    [GeneratedRegex(@"mackintosh|\balex\b", RegexOptions.IgnoreCase)]
    private static partial Regex AlexPattern();

    [GeneratedRegex(@"liddy|\bcasey\b", RegexOptions.IgnoreCase)]
    private static partial Regex CaseyPattern();
}
