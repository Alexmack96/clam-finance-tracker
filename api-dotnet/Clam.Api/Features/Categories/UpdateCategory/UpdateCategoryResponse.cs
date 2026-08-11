namespace Clam.Api.Features.Categories.UpdateCategory;

/// The updated category. Same three fields as the create slice returns, and
/// deliberately its own type: these two slices are free to diverge, and sharing
/// one model would mean the first divergence has to argue with the other slice.
public sealed class UpdateCategoryResponse
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
}
