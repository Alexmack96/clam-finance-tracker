using Ardalis.Result;
using Clam.Api.Infrastructure.Monzo;
using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Monzo.SyncMonzo;

public sealed class SyncMonzoEndpoint(SyncMonzoCommand command, MonzoOptions options)
    : ResultEndpointWithoutRequest<SyncMonzoResponse>
{
    public override void Configure()
    {
        Post("admin/monzo/sync");
        Description(b => b.WithName("SyncMonzo"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Not configured is a 503 rather than a 500: nothing is broken, the
        // connection was simply never set up.
        if (!options.IsConfigured)
        {
            await SendResultAsync(Result<SyncMonzoResponse>.Unavailable(MonzoOptionsMessages.NotConfigured), ct);
            return;
        }

        await SendResultAsync(await command.ExecuteAsync(ct), ct);
    }
}
