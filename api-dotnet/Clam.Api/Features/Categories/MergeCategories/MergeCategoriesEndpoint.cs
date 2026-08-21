using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Categories.MergeCategories;

public sealed class MergeCategoriesEndpoint(MergeCategoriesCommand command)
    : ResultEndpoint<MergeCategoriesRequest, MergeCategoriesResponse>
{
    public override void Configure()
    {
        Post("categories/{FromId}/merge/{ToId}");
        Description(b => b.WithName("MergeCategories"));
    }

    public override async Task HandleAsync(MergeCategoriesRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}
