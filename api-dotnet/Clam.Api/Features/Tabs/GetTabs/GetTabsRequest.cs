namespace Clam.Api.Features.Tabs.GetTabs;

/// `?status=all` shows settled tabs too; anything else (including absent) shows
/// only open ones. A string rather than an enum because that is the whole of the
/// contract — there is no `?status=Settled`.
public sealed class GetTabsRequest
{
    public string? Status { get; set; }
}
