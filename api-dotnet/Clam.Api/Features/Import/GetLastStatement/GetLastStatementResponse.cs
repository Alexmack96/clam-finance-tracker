using System.Text.Json.Serialization;

namespace Clam.Api.Features.Import.GetLastStatement;

/// For each statement-based bank, the date of its most recent processed
/// transaction — how far the uploaded statements reach.
///
/// Date-only strings ("2026-07-31"), not timestamps: this answers "which
/// statement have I got up to", and a time of day would be noise.
public sealed class GetLastStatementResponse
{
    public string? Monzo { get; set; }
    public string? Amex { get; set; }
    public string? Barclays { get; set; }
    public string? Santander { get; set; }
    public string? Hsbc { get; set; }
    public string? Sofi { get; set; }
    public string? Chase { get; set; }

    /// Amex is shared between owners, so its latest date is also broken down per
    /// person — the import card shows the right "statements through" date for
    /// whoever is selected.
    [JsonPropertyName("amexByOwner")]
    public Dictionary<string, string?> AmexByOwner { get; set; } = [];
}
