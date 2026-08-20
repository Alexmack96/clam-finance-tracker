namespace Clam.Api.Features.Rules.ApplyRules;

/// What the run actually did. `Affected` is the row count, which is not
/// `CategoryChanges + BucketChanges` — one transaction can have both fields
/// rewritten by the same run.
public sealed class ApplyRulesResponse
{
    public int CategoryChanges { get; set; }
    public int BucketChanges { get; set; }
    public int Affected { get; set; }
}
