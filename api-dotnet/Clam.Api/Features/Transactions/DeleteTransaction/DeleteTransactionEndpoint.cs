using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Transactions.DeleteTransaction;

public sealed class DeleteTransactionEndpoint(DeleteTransactionCommand command)
    : ResultEndpointWithoutResponse<DeleteTransactionRequest>
{
    public override void Configure()
    {
        Delete("transactions/{Id}");
        Description(b => b.WithName("DeleteTransaction"));
    }

    public override async Task HandleAsync(DeleteTransactionRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
