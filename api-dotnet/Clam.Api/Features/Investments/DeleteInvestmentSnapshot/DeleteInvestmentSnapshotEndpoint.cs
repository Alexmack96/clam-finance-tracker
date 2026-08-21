using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Investments.DeleteInvestmentSnapshot;

public sealed class DeleteInvestmentSnapshotEndpoint(DeleteInvestmentSnapshotCommand command)
    : ResultEndpointWithoutResponse<DeleteInvestmentSnapshotRequest>
{
    public override void Configure()
    {
        Delete("investments/snapshots/{Id}");
        Description(b => b.WithName("DeleteInvestmentSnapshot"));
    }

    public override async Task HandleAsync(DeleteInvestmentSnapshotRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
