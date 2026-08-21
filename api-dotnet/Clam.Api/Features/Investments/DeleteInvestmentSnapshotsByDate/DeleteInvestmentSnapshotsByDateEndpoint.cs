using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Investments.DeleteInvestmentSnapshotsByDate;

public sealed class DeleteInvestmentSnapshotsByDateEndpoint(DeleteInvestmentSnapshotsByDateCommand command)
    : ResultEndpointWithoutResponse<DeleteInvestmentSnapshotsByDateRequest>
{
    public override void Configure()
    {
        // Three segments, so this can never be confused with the two-segment
        // delete-by-id route despite sharing a prefix.
        Delete("investments/snapshots/date/{Date}");
        Description(b => b.WithName("DeleteInvestmentSnapshotsByDate"));
    }

    public override async Task HandleAsync(DeleteInvestmentSnapshotsByDateRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
