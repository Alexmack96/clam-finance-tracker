namespace Clam.Api.Tests.Features;

/// The slice with the most logic in SQL rather than C#, so the one where a
/// regression is least likely to be obvious by reading the code.
public class GetDashboardSummaryTests(ClamApp app) : ApiTestBase<ClamApp>(app)
{
    private const string Url = "/api/dashboard/summary";

    [Fact]
    public async Task Counts_only_caseys_monzo_net_income_as_settlement_in()
    {
        using var json = await GetJsonAsync(Url);

        // 600.00 from the one Casey/Income/Net/monzo: row. Alex's salary is also
        // Income with a monzo: id and must not be counted.
        json.RootElement.GetProperty("caseyIn").GetString().ShouldBe("600");
    }

    [Fact]
    public async Task Sums_joint_expenses_across_categories()
    {
        using var json = await GetJsonAsync(Url);

        // 1000.50 rent + 200.00 groceries.
        json.RootElement.GetProperty("jointExpenses").GetString().ShouldBe("1200.5");
    }

    [Fact]
    public async Task Settlement_is_casey_in_minus_half_the_joint_expenses()
    {
        using var json = await GetJsonAsync(Url);

        // 600.00 - (1200.50 / 2) = -0.25. A negative value is the interesting
        // case: it means Casey has underpaid, and the sign has to survive the
        // decimal-as-string conversion.
        json.RootElement.GetProperty("settlement").GetString().ShouldBe("-0.25");
    }

    [Fact]
    public async Task Breaks_joint_spending_down_by_category_largest_first()
    {
        using var json = await GetJsonAsync(Url);

        var spending = json.RootElement.GetProperty("spendingByCategory").EnumerateArray().ToArray();

        spending.Length.ShouldBe(2);
        spending[0].GetProperty("name").GetString().ShouldBe("Rent");
        spending[0].GetProperty("value").GetString().ShouldBe("1000.5");
        spending[0].GetProperty("color").GetString().ShouldBe("#ef4444");
        spending[1].GetProperty("name").GetString().ShouldBe("Groceries");
        spending[1].GetProperty("value").GetString().ShouldBe("200");
    }

    [Fact]
    public async Task Excludes_non_joint_and_income_rows_from_the_breakdown()
    {
        using var json = await GetJsonAsync(Url);

        var names = json.RootElement
            .GetProperty("spendingByCategory")
            .EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToArray();

        names.ShouldNotContain("Salary");   // Alex, Income
        names.ShouldNotContain("Net");      // Casey, Income
    }
}
