using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Categories.DeleteCategory;

public sealed class DeleteCategoryEndpoint(DeleteCategoryCommand command)
    : ResultEndpointWithoutResponse<DeleteCategoryRequest>
{
    public override void Configure()
    {
        Delete("categories/{Id}");
        Description(b => b.WithName("DeleteCategory"));
    }

    public override async Task HandleAsync(DeleteCategoryRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
