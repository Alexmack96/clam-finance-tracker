using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Investments.UpsertInvestmentSnapshot;

public sealed class UpsertInvestmentSnapshotEndpoint(UpsertInvestmentSnapshotCommand command)
    : ResultEndpoint<UpsertInvestmentSnapshotRequest, InvestmentSnapshotRecord>
{
    public override void Configure()
    {
        Put("investments/snapshots");
        Description(b => b.WithName("UpsertInvestmentSnapshot"));
    }

    public override async Task HandleAsync(UpsertInvestmentSnapshotRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
