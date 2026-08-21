using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Tabs.DeleteTab;

public sealed class DeleteTabEndpoint(DeleteTabCommand command) : ResultEndpointWithoutResponse<DeleteTabRequest>
{
    public override void Configure()
    {
        Delete("tabs/{Id}");
        Description(b => b.WithName("DeleteTab"));
    }

    public override async Task HandleAsync(DeleteTabRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
