using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Import.ImportChase;

public sealed class ImportChaseEndpoint(ImportChaseCommand command)
    : ResultEndpoint<ImportChaseRequest, ImportChaseResponse>
{
    public override void Configure()
    {
        Post("admin/import/chase");
        AllowFileUploads();
        Description(b => b.WithName("ImportChase"));
    }

    public override async Task HandleAsync(ImportChaseRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
