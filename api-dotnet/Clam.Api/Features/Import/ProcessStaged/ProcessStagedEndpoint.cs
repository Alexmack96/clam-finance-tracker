using FastEndpoints;

namespace Clam.Api.Features.Import.ProcessStaged;

public sealed class ProcessStagedEndpoint(ProcessStagedCommand command)
    : EndpointWithoutRequest<ProcessStagedResponse>
{
    public override void Configure()
    {
        Post("admin/process");
        AllowAnonymous();
        Description(b => b.WithName("ProcessStaged"));
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await Send.OkAsync(await command.ExecuteAsync(ct), ct);
}
