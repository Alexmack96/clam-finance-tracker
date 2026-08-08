namespace Clam.Api.Features.Categories.GetCategories;

/// Serialises as a bare JSON array to match `res.json(categories.map(...))` in
/// the Express route. See GetTransactionsResponse for why the collection is the
/// response type rather than a property on it.
public sealed class GetCategoriesResponse : List<CategoryListItem>
{
    public GetCategoriesResponse(IEnumerable<CategoryListItem> rows) : base(rows) { }
}
