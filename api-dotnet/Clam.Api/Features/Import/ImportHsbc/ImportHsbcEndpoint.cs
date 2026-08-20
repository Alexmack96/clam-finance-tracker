using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Import.ImportHsbc;

public sealed class ImportHsbcEndpoint(ImportHsbcCommand command)
    : ResultEndpoint<ImportHsbcRequest, ImportHsbcResponse>
{
    public override void Configure()
    {
        Post("admin/import/hsbc");
        AllowFileUploads();
        AllowAnonymous();
        Description(b => b.WithName("ImportHsbc"));
    }

    public override async Task HandleAsync(ImportHsbcRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
