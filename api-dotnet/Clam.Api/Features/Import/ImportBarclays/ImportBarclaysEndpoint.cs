using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Import.ImportBarclays;

public sealed class ImportBarclaysEndpoint(ImportBarclaysCommand command)
    : ResultEndpoint<ImportBarclaysRequest, ImportBarclaysResponse>
{
    public override void Configure()
    {
        Post("admin/import/barclays");
        AllowFileUploads();
        Description(b => b.WithName("ImportBarclays"));
    }

    public override async Task HandleAsync(ImportBarclaysRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
