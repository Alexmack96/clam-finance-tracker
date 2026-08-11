namespace Clam.Api.Tests.Features;

public class GetTabsTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Lists_open_tabs_and_totals_them_by_direction()
    {
        await Given.TabsAsync();
        var response = await Get("/api/tabs");
        await Verify(response);
    }

    [Fact]
    public async Task Lists_settled_tabs_too_when_asked_for_all()
    {
        await Given.TabsAsync();
        var response = await Get("/api/tabs?status=all");
        await Verify(response);
    }

    [Fact]
    public async Task Totals_are_zero_when_there_are_no_tabs()
    {
        await Given.NothingAsync();
        var response = await Get("/api/tabs");
        await Verify(response);
    }
}

public class CreateTabTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Creates_a_tab()
    {
        await Given.NothingAsync();
        var response = await Post("/api/tabs",
            new { person = "Sam", description = "dinner", amount = 20.5, direction = "TheyOwe" });
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_an_amount_that_is_not_positive()
    {
        await Given.NothingAsync();
        var response = await Post("/api/tabs",
            new { person = "Sam", description = "dinner", amount = -1, direction = "IOwe" });
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_a_tab_with_no_person()
    {
        await Given.NothingAsync();
        var response = await Post("/api/tabs",
            new { person = "", description = "dinner", amount = 5, direction = "IOwe" });
        await Verify(response);
    }
}

public class UpdateTabTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Stamps_settled_at_when_a_tab_is_settled()
    {
        await Given.TabsAsync();
        var response = await Patch($"/api/tabs/{Arrange.OpenTabId}", new { status = "Settled" });
        await Verify(response);
    }

    [Fact]
    public async Task Clears_settled_at_when_a_tab_is_reopened()
    {
        await Given.TabsAsync();
        var response = await Patch($"/api/tabs/{Arrange.SettledTabId}", new { status = "Open" });
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_tab()
    {
        await Given.TabsAsync();
        var response = await Patch("/api/tabs/nope", new { status = "Settled" });
        await Verify(response);
    }
}

public class DeleteTabTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Deletes_a_tab()
    {
        await Given.TabsAsync();
        var response = await Delete($"/api/tabs/{Arrange.OpenTabId}");
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_tab()
    {
        await Given.TabsAsync();
        var response = await Delete("/api/tabs/nope");
        await Verify(response);
    }
}
