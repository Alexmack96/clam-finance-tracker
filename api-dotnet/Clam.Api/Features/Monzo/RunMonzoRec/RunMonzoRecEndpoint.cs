using Ardalis.Result;
using Clam.Api.Infrastructure.Monzo;
using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Monzo.RunMonzoRec;

public sealed class RunMonzoRecEndpoint(RunMonzoRecCommand command, MonzoOptions options)
    : ResultEndpointWithoutRequest<MonzoRecRun>
{
    public override void Configure()
    {
        Post("admin/monzo/rec/run");
        Description(b => b.WithName("RunMonzoRec"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!options.IsConfigured)
        {
            await SendResultAsync(Result<MonzoRecRun>.Unavailable(MonzoOptionsMessages.NotConfigured), ct);
            return;
        }

        await SendResultAsync(await command.ExecuteAsync(ct), ct);
    }
}
