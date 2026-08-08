using System.Text.Json;

namespace Clam.Api.Tests.Features;

/// Asserted against raw JSON rather than a deserialised DTO on purpose.
///
/// This service exists to be byte-compatible with the Express API it replaces.
/// Deserialising into a C# type would happily turn the number 1000.5 back into a
/// decimal and hide exactly the regression these tests are for — the client
/// types `amount` as a string and does string maths on it.
public class GetTransactionsTests(ClamApp app) : ApiTestBase<ClamApp>(app)
{
    [Fact]
    public async Task Returns_a_bare_array_not_a_wrapper_object()
    {
        using var json = await GetJsonAsync("/api/transactions");

        json.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
        json.RootElement.GetArrayLength().ShouldBe(4);
    }

    [Fact]
    public async Task Orders_by_date_descending()
    {
        using var json = await GetJsonAsync("/api/transactions");

        var descriptions = json.RootElement
            .EnumerateArray()
            .Select(e => e.GetProperty("description").GetString())
            .ToArray();

        descriptions[0].ShouldBe("RENT PAYMENT");        // 2026-03-10, newest
        descriptions[^1].ShouldBe("ACME LTD SALARY");    // 2026-01-05, oldest
    }

    [Fact]
    public async Task Serialises_amount_as_a_string_with_prisma_scale()
    {
        using var json = await GetJsonAsync("/api/transactions");

        var amount = FindByDescription(json, "RENT PAYMENT").GetProperty("amount");

        // DECIMAL(18,2) holds 1000.50; decimal.js's toJSON emits "1000.5".
        amount.ValueKind.ShouldBe(JsonValueKind.String);
        amount.GetString().ShouldBe("1000.5");
    }

    [Fact]
    public async Task Serialises_date_as_utc_with_a_trailing_z()
    {
        using var json = await GetJsonAsync("/api/transactions");

        FindByDescription(json, "RENT PAYMENT")
            .GetProperty("date")
            .GetString()
            .ShouldBe("2026-03-10T00:00:00.000Z");
    }

    [Fact]
    public async Task Nests_the_category_without_a_transaction_count()
    {
        using var json = await GetJsonAsync("/api/transactions");

        var category = FindByDescription(json, "RENT PAYMENT").GetProperty("category");

        category.GetProperty("name").GetString().ShouldBe("Rent");
        category.GetProperty("color").GetString().ShouldBe("#ef4444");
        // The nested shape is Prisma's `include: { category: true }`, which has
        // no _count. That is why TransactionCategory is a separate type from the
        // categories slice's CategoryListItem.
        category.TryGetProperty("transactionCount", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("type=Expense", 2)]
    [InlineData("type=Income", 2)]
    [InlineData("owner=Joint", 2)]
    [InlineData("owner=Alex", 1)]
    [InlineData("type=Expense&owner=Joint", 2)]
    [InlineData("type=Income&owner=Joint", 0)]
    public async Task Filters_narrow_the_result(string query, int expected)
    {
        using var json = await GetJsonAsync($"/api/transactions?{query}");

        json.RootElement.GetArrayLength().ShouldBe(expected);
    }

    [Fact]
    public async Task Unknown_filter_value_returns_an_empty_list_not_a_400()
    {
        // Locks in the documented compatibility choice on GetTransactionsRequest:
        // the Express route passes req.query.type to Prisma unvalidated, so a
        // nonsense value matches nothing. Binding to the enum here would turn
        // this into a 400 and break every caller relying on the old behaviour.
        using var json = await GetJsonAsync("/api/transactions?type=Nonsense");

        json.RootElement.GetArrayLength().ShouldBe(0);
    }

    private static JsonElement FindByDescription(JsonDocument json, string description)
        => json.RootElement
            .EnumerateArray()
            .Single(e => e.GetProperty("description").GetString() == description);
}
