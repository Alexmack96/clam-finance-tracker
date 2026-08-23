using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Import.ImportSofi;

public sealed class ImportSofiEndpoint(ImportSofiCommand command)
    : ResultEndpoint<ImportSofiRequest, ImportSofiResponse>
{
    public override void Configure()
    {
        Post("admin/import/sofi");
        AllowFileUploads();
        Description(b => b.WithName("ImportSofi"));
    }

    public override async Task HandleAsync(ImportSofiRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
