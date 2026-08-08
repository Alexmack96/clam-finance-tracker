namespace Clam.Api.Features.Categories.GetCategories;

/// A category as the list endpoint returns it — with the usage count that
/// `include: { _count: { select: { transactions: true } } }` produces in the
/// Express route, flattened to `transactionCount` exactly as that route does.
public sealed class CategoryListItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public int TransactionCount { get; set; }
}
