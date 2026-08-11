using System.Text.Json.Serialization;
using Clam.Api.Domain;

namespace Clam.Api.Features.Rules.PreviewRules;

/// One transaction the run would change.
public sealed class RulePreviewRow
{
    public string Id { get; set; } = "";
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";

    /// A JSON number here, not a string. The dry-run is a report the client
    /// sorts and sums; it is not the transaction record, which is where the
    /// decimal-as-string convention applies.
    public double Amount { get; set; }

    public TransactionType Type { get; set; }
    public string? Bank { get; set; }

    /// An em dash when the current category cannot be resolved, matching the
    /// Express route — the client renders this straight into a cell.
    public string CurrentCategory { get; set; } = "";

    public string? ProposedCategory { get; set; }
    public Bucket? CurrentBucket { get; set; }
    public Bucket? ProposedBucket { get; set; }

    /// Which rule won, per field — null when nothing claimed it.
    public string? CategoryRuleId { get; set; }
    public string? BucketRuleId { get; set; }
}

public sealed class PreviewRulesResponse
{
    /// Rows the run would actually change.
    public IReadOnlyList<RulePreviewRow> Rows { get; set; } = [];

    public int Scanned { get; set; }
    public int CategoryChanges { get; set; }
    public int BucketChanges { get; set; }

    /// Rows skipped because the field was pinned by hand.
    public int PinnedSkipped { get; set; }

    /// Single-rule previews only, and absent rather than zero on a whole-set
    /// preview — the Express route spreads them in, so the key is simply not
    /// there. `Matched` counts every transaction the rule's conditions accept;
    /// `Won` counts the subset no higher-positioned rule claimed first.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Matched { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Won { get; set; }
}
