using Clam.Api.Domain;

namespace Clam.Api.Features.Transactions.UpdateTransaction;

/// The updated row with its category nested, matching Prisma's
/// `include: { category: true }` on the update.
///
/// Its own type rather than the list slice's <c>TransactionRecord</c>: the two
/// happen to agree today, and the moment the list endpoint grows a projection
/// this slice would be dragged along with it.
public sealed class UpdateTransactionResponse
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public DateTime Date { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CategoryId { get; set; } = "";
    public string? ExternalId { get; set; }
    public string? Note { get; set; }
    public Owner Owner { get; set; }
    public bool Reviewed { get; set; }
    public Bucket? Bucket { get; set; }
    public decimal? OriginalAmount { get; set; }
    public string? OriginalCurrency { get; set; }
    public string? StatementFileId { get; set; }

    public UpdatedTransactionCategory Category { get; set; } = new();
}

public sealed class UpdatedTransactionCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
}
