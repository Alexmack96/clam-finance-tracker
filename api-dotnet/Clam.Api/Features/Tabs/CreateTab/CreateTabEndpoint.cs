using FastEndpoints;

namespace Clam.Api.Features.Tabs.CreateTab;

public sealed class CreateTabEndpoint(CreateTabCommand command) : Endpoint<CreateTabRequest, TabRecord>
{
    public override void Configure()
    {
        Post("tabs");
        Description(b => b.WithName("CreateTab"));
    }

    public override async Task HandleAsync(CreateTabRequest req, CancellationToken ct)
        => await Send.ResponseAsync(await command.ExecuteAsync(req, ct), StatusCodes.Status201Created, ct);
}
