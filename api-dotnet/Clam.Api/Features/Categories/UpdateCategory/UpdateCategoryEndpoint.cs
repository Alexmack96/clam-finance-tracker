using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Categories.UpdateCategory;

public sealed class UpdateCategoryEndpoint(UpdateCategoryCommand command)
    : ResultEndpoint<UpdateCategoryRequest, UpdateCategoryResponse>
{
    public override void Configure()
    {
        Patch("categories/{Id}");
        AllowAnonymous();
        Description(b => b.WithName("UpdateCategory"));
    }

    public override async Task HandleAsync(UpdateCategoryRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
