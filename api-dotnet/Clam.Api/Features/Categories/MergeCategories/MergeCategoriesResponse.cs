namespace Clam.Api.Features.Categories.MergeCategories;

/// Counts, not a category — a merge is a thing that happened, and the client
/// reports it rather than rendering a row.
public sealed class MergeCategoriesResponse
{
    public int Merged { get; set; }
    public int RulesRepointed { get; set; }
    public int ConditionsRewritten { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}
