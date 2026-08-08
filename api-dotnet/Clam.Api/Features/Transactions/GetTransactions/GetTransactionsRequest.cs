namespace Clam.Api.Features.Transactions.GetTransactions;

/// Bound from the query string by FastEndpoints, by property name.
///
/// All three are strings rather than the Domain enums on purpose. The Express
/// route passes `req.query.type` to Prisma unvalidated, so an unknown value
/// returns an empty list rather than an error. Binding to `TransactionType?`
/// here would turn that into a 400 and change the contract; the SQL simply
/// matches nothing instead, exactly like today.
public sealed class GetTransactionsRequest
{
    public string? Type { get; set; }
    public string? CategoryId { get; set; }
    public string? Owner { get; set; }
}
