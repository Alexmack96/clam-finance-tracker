using System.Globalization;
using System.Text.RegularExpressions;

namespace Clam.Api.Features.Import;

/// Staged amounts are the strings the statement carried, so parsing them is
/// where a bad row becomes an errored row rather than an exception.
internal static partial class StagedAmount
{
    /// Mirrors JavaScript's `parseFloat` after stripping thousands separators,
    /// which is what the Express pipeline does. That leniency is deliberate and
    /// load-bearing: a parser that appended a stray character to an otherwise
    /// good amount would, under `decimal.Parse`, turn one salvageable row into an
    /// errored one and change what the two services import from the same file.
    internal static decimal? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var match = LeadingNumber().Match(value.Replace(",", "", StringComparison.Ordinal).Trim());
        if (!match.Success) return null;

        return decimal.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// Staged dates are strings too, and a non-HSBC PDF that slipped past the
    /// upload guard is the case this exists for.
    internal static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    [GeneratedRegex(@"^[+-]?(\d+\.?\d*|\.\d+)")]
    private static partial Regex LeadingNumber();

    /// A SoFi row that moves money between that bank's own accounts. Not
    /// spending — importing it would count the same pounds twice.
    internal static bool IsInternalTransfer(string description) =>
        InternalTransfer().IsMatch(description);

    [GeneratedRegex(@"^(From|To)\s+(Savings|Checking)", RegexOptions.IgnoreCase)]
    private static partial Regex InternalTransfer();
}
