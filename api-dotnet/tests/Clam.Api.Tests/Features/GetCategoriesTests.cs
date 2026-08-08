namespace Clam.Api.Tests.Features;

public class GetCategoriesTests(ClamApp app) : ApiTestBase<ClamApp>(app)
{
    [Fact]
    public async Task Returns_every_category_ordered_by_name()
    {
        using var json = await GetJsonAsync("/api/categories");

        var names = json.RootElement
            .EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToArray();

        names.ShouldBe(["Groceries", "Net", "Rent", "Salary", "Unused"]);
    }

    [Fact]
    public async Task Keeps_a_category_with_no_transactions_at_zero()
    {
        using var json = await GetJsonAsync("/api/categories");

        var unused = json.RootElement
            .EnumerateArray()
            .Single(e => e.GetProperty("name").GetString() == "Unused");

        // The query uses a LEFT JOIN precisely so this row survives. An INNER
        // JOIN would drop it silently and the only symptom would be a category
        // missing from a dropdown.
        unused.GetProperty("transactionCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Flattens_the_prisma_count_to_transaction_count()
    {
        using var json = await GetJsonAsync("/api/categories");

        var rent = json.RootElement
            .EnumerateArray()
            .Single(e => e.GetProperty("name").GetString() == "Rent");

        rent.GetProperty("transactionCount").GetInt32().ShouldBe(1);
        rent.GetProperty("color").GetString().ShouldBe("#ef4444");
    }
}
