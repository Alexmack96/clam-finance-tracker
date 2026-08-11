namespace Clam.Api.Features.Categories.MergeCategories;

public sealed class MergeCategoriesRequest
{
    public string FromId { get; set; } = "";
    public string ToId { get; set; } = "";
}
