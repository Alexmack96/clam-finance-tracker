using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Import.ImportAmex;

public sealed class ImportAmexEndpoint(ImportAmexCommand command)
    : ResultEndpoint<ImportAmexRequest, ImportAmexResponse>
{
    public override void Configure()
    {
        Post("admin/import/amex");
        AllowFileUploads();
        Description(b => b.WithName("ImportAmex"));
    }

    public override async Task HandleAsync(ImportAmexRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
