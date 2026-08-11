namespace Clam.Api.Features.Categories.CreateCategory;

/// The created category, exactly as Prisma's `category.create` returns it — no
/// `transactionCount`, because a category that has just been created has none
/// and the Express route does not compute one.
public sealed class CreateCategoryResponse
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
}
