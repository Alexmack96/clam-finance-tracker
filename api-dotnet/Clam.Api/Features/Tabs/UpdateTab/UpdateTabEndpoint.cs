using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Tabs.UpdateTab;

public sealed class UpdateTabEndpoint(UpdateTabCommand command) : ResultEndpoint<UpdateTabRequest, TabRecord>
{
    public override void Configure()
    {
        Patch("tabs/{Id}");
        Description(b => b.WithName("UpdateTab"));
    }

    public override async Task HandleAsync(UpdateTabRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
