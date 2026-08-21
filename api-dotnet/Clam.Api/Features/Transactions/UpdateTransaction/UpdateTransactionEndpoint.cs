using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Transactions.UpdateTransaction;

public sealed class UpdateTransactionEndpoint(UpdateTransactionCommand command)
    : ResultEndpoint<UpdateTransactionRequest, UpdateTransactionResponse>
{
    public override void Configure()
    {
        Patch("transactions/{Id}");
        Description(b => b.WithName("UpdateTransaction"));
    }

    public override async Task HandleAsync(UpdateTransactionRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
