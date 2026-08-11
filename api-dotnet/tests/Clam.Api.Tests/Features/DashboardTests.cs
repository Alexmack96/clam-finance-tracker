namespace Clam.Api.Tests.Features;

public class GetDashboardSummaryTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Reports_the_settlement_between_the_two_owners()
    {
        await Given.SeededAsync();
        var response = await Get("/api/dashboard/summary");
        await Verify(response);
    }

    [Fact]
    public async Task Reports_zeroes_when_there_are_no_transactions()
    {
        await Given.NothingAsync();
        var response = await Get("/api/dashboard/summary");
        await Verify(response);
    }
}

public class GetDashboardAnalyticsTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Reports_every_series_up_to_the_current_month()
    {
        await Given.SeededAsync();
        var response = await Get("/api/dashboard/analytics?owner=Alex");
        await Verify(response);
    }

    [Fact]
    public async Task Falls_back_to_a_flat_budget_when_the_owner_has_no_salary()
    {
        await Given.SeededAsync();
        var response = await Get("/api/dashboard/analytics?owner=Casey");
        await Verify(response);
    }

    [Fact]
    public async Task Reports_empty_series_when_there_are_no_transactions()
    {
        await Given.NothingAsync();
        var response = await Get("/api/dashboard/analytics?owner=Alex");
        await Verify(response);
    }
}

public class GetUtilitiesTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Groups_this_months_bills_by_who_paid_them()
    {
        await Given.UtilitiesThisMonthAsync();
        var response = await Get("/api/utilities");
        await Verify(response);
    }

    [Fact]
    public async Task Reports_nothing_when_no_utility_categories_exist()
    {
        await Given.NothingAsync();
        var response = await Get("/api/utilities");
        await Verify(response);
    }
}

public class HealthTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Reports_ok_without_touching_the_database()
    {
        await Given.NothingAsync();
        var response = await Get("/api/health");
        await Verify(response);
    }
}
