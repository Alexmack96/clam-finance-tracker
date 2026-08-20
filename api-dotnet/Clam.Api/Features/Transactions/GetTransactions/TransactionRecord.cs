using Clam.Api.Domain;

namespace Clam.Api.Features.Transactions.GetTransactions;

/// The read model for this slice, and deliberately not a shared "Transaction
/// entity". It exists to be the row Dapper materialises *and* the object that
/// goes on the wire, because for a read-only endpoint those are the same shape
/// and an extra mapping layer would only be ceremony.
///
/// Property order is the JSON property order, so it mirrors the Prisma model
/// field for field — that is what keeps the response byte-comparable with the
/// Express API this slice replaces.
public sealed class TransactionRecord
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

    /// Prisma's `include: { category: true }` nests the whole category. Dapper
    /// fills this via multi-mapping rather than a second round trip.
    public TransactionCategory Category { get; set; } = new();
}
