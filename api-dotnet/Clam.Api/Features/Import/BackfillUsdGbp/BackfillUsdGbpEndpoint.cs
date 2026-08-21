using FastEndpoints;

namespace Clam.Api.Features.Import.BackfillUsdGbp;

public sealed class BackfillUsdGbpEndpoint(BackfillUsdGbpCommand command)
    : Endpoint<BackfillUsdGbpRequest, BackfillUsdGbpResponse>
{
    public override void Configure()
    {
        Post("admin/backfill/usd-gbp");
        Description(b => b.WithName("BackfillUsdGbp"));
    }

    public override async Task HandleAsync(BackfillUsdGbpRequest req, CancellationToken ct)
        => await Send.OkAsync(await command.ExecuteAsync(req, ct), ct);
}
