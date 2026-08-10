using FastEndpoints;

namespace Clam.Api.Features.Categories.GetCategories;

public sealed class GetCategoriesEndpoint(GetCategoriesQuery query): EndpointWithoutRequest<GetCategoriesResponse>
{
    public override void Configure()
    {
        Get("categories");
        AllowAnonymous();
        Description(b => b.WithName("GetCategories"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var rows = await query.ExecuteAsync(ct);
        await Send.OkAsync(new GetCategoriesResponse(rows), ct);
    }
}
