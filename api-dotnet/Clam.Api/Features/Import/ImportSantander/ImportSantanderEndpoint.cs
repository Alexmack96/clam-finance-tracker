using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Import.ImportSantander;

public sealed class ImportSantanderEndpoint(ImportSantanderCommand command)
    : ResultEndpoint<ImportSantanderRequest, ImportSantanderResponse>
{
    public override void Configure()
    {
        Post("admin/import/santander");
        AllowFileUploads();
        Description(b => b.WithName("ImportSantander"));
    }

    public override async Task HandleAsync(ImportSantanderRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
