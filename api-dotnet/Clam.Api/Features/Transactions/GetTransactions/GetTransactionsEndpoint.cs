using FastEndpoints;

namespace Clam.Api.Features.Transactions.GetTransactions;

public sealed class GetTransactionsEndpoint(GetTransactionsQuery query)
    : Endpoint<GetTransactionsRequest, GetTransactionsResponse>
{
    public override void Configure()
    {
        Get("transactions");

        // Deliberate, not an oversight. This service is a read-only spike over a
        // database that holds only synthetic data, and Better Auth sessions live
        // in the *other* database, so there is nothing here it could validate
        // against. The moment real data lands, this line has to go first.
        AllowAnonymous();

        Description(b => b.WithName("GetTransactions"));
    }

    public override async Task HandleAsync(GetTransactionsRequest req, CancellationToken ct)
    {
        var rows = await query.ExecuteAsync(req, ct);
        await Send.OkAsync(new GetTransactionsResponse(rows), ct);
    }
}
