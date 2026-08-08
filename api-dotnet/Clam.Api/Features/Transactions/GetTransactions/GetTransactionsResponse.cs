namespace Clam.Api.Features.Transactions.GetTransactions;

/// A named response type that still serialises as a bare JSON array.
///
/// The Express route does `res.json(transactions)`, so the client receives an
/// array at the root. Wrapping the rows in an object — the more usual REPR
/// shape — would be a breaking change for every caller. Deriving the response
/// from List keeps the wire format identical while still giving the endpoint a
/// declared, discoverable response type.
public sealed class GetTransactionsResponse : List<TransactionRecord>
{
    public GetTransactionsResponse(IEnumerable<TransactionRecord> rows) : base(rows) { }
}
