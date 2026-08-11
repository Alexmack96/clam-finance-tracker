using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Categories.CreateCategory;

public sealed class CreateCategoryEndpoint(CreateCategoryCommand command)
    : ResultEndpoint<CreateCategoryRequest, CreateCategoryResponse>
{
    public override void Configure()
    {
        Post("categories");
        AllowAnonymous();
        Description(b => b.WithName("CreateCategory"));
    }

    public override async Task HandleAsync(CreateCategoryRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
