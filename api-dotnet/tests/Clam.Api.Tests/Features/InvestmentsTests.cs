namespace Clam.Api.Tests.Features;

public class GetInvestmentsTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Reports_nav_excluding_pension_and_total_wealth_including_it()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Get("/api/investments?owner=Alex");
        await Verify(response);
    }

    [Fact]
    public async Task Reports_zeroes_for_an_owner_with_no_accounts()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Get("/api/investments?owner=Casey");
        await Verify(response);
    }
}

public class CreateInvestmentAccountTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Creates_an_account_at_the_bottom_of_the_list()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Post("/api/investments/accounts", new { name = "Freetrade", category = "equity" });
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_a_name_this_owner_already_uses()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Post("/api/investments/accounts",
            new { name = "Vanguard", category = "equity", owner = "Alex" });
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_a_category_that_is_not_one_of_the_six()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Post("/api/investments/accounts",
            new { name = "Mystery", category = "beanie-babies" });
        await Verify(response);
    }
}

public class UpdateInvestmentAccountTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Updates_the_rate_and_leaves_the_rest_alone()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Patch("/api/investments/accounts/cacct00000000000002cash", new { rate = 3.9 });
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_account()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Patch("/api/investments/accounts/nope", new { rate = 1.0 });
        await Verify(response);
    }
}

public class DeleteInvestmentAccountTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Deletes_an_account_and_its_snapshots()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Delete("/api/investments/accounts/cacct0000000000001equity");
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_account()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Delete("/api/investments/accounts/nope");
        await Verify(response);
    }
}

public class UpsertInvestmentSnapshotTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Records_a_new_snapshot()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Put("/api/investments/snapshots",
            new { accountId = "cacct0000000000001equity", date = "2026-03-31T00:00:00Z", value = 1300.0 });
        await Verify(response);
    }

    [Fact]
    public async Task Corrects_a_snapshot_that_already_exists_for_that_date()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Put("/api/investments/snapshots",
            new { accountId = "cacct0000000000001equity", date = "2026-01-31T00:00:00Z", value = 1111.0 });
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_account()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Put("/api/investments/snapshots",
            new { accountId = "nope", date = "2026-01-31T00:00:00Z", value = 1.0 });
        await Verify(response);
    }
}

public class DeleteInvestmentSnapshotTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Deletes_one_snapshot()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Delete("/api/investments/snapshots/csnap0000000000000000001");
        await Verify(response);
    }

    [Fact]
    public async Task Deletes_a_whole_date_for_one_owner()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Delete("/api/investments/snapshots/date/2026-01-31?owner=Alex");
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_snapshot()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Delete("/api/investments/snapshots/nope");
        await Verify(response);
    }
}
