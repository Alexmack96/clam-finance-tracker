using FastEndpoints;

namespace Clam.Api.Features.Tabs.GetTabs;

public sealed class GetTabsEndpoint(GetTabsQuery query) : Endpoint<GetTabsRequest, GetTabsResponse>
{
    public override void Configure()
    {
        Get("tabs");
        AllowAnonymous();
        Description(b => b.WithName("GetTabs"));
    }

    public override async Task HandleAsync(GetTabsRequest req, CancellationToken ct)
        => await Send.OkAsync(await query.ExecuteAsync(req, ct), ct);
}
