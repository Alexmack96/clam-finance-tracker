namespace Clam.Api.Features.Transactions.GetTransactions;

/// The category as it appears *nested inside a transaction*. Intentionally
/// separate from the category model in the GetCategories slice: that one carries
/// a `transactionCount`, this one must not, because Prisma's nested include does
/// not return one. Two slices, two shapes, no shared type to compromise between
/// them — which is the point of slicing this way.
public sealed class TransactionCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
}
