namespace Clam.Api.Tests.Features;

public class GetTransactionsTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Lists_every_transaction_newest_first()
    {
        await Given.SeededAsync();
        var response = await Get("/api/transactions");
        await Verify(response);
    }

    [Fact]
    public async Task Filters_by_type()
    {
        await Given.SeededAsync();
        var response = await Get("/api/transactions?type=Income");
        await Verify(response);
    }

    [Fact]
    public async Task Filters_by_owner_and_category_together()
    {
        await Given.SeededAsync();
        var response = await Get($"/api/transactions?owner=Joint&categoryId={Arrange.RentCategoryId}");
        await Verify(response);
    }

    [Fact]
    public async Task Returns_nothing_for_a_filter_value_that_is_not_an_enum_member()
    {
        await Given.SeededAsync();
        var response = await Get("/api/transactions?type=Nonsense");
        await Verify(response);
    }
}

public class UpdateTransactionTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Updates_a_field_and_returns_the_row_with_its_category()
    {
        await Given.SeededAsync();
        var response = await Patch($"/api/transactions/{Arrange.RentTransactionId}", new { reviewed = false });
        await Verify(response);
    }

    [Fact]
    public async Task Sets_the_category_when_it_is_sent()
    {
        await Given.SeededAsync();
        var response = await Patch($"/api/transactions/{Arrange.RentTransactionId}",
            new { categoryId = Arrange.GroceriesCategoryId });
        await Verify(response);
    }

    [Fact]
    public async Task Sets_the_bucket_when_it_is_sent()
    {
        await Given.SeededAsync();
        var response = await Patch($"/api/transactions/{Arrange.RentTransactionId}", new { bucket = "Wants" });
        await Verify(response);
    }

    [Fact]
    public async Task Clears_the_note_when_one_is_sent_as_null()
    {
        await Given.SeededAsync();
        var response = await Patch($"/api/transactions/{Arrange.RentTransactionId}", new { note = (string?)null });
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_transaction()
    {
        await Given.SeededAsync();
        var response = await Patch("/api/transactions/nope", new { reviewed = true });
        await Verify(response);
    }

    /// Rent is a manual row with no externalId, so it is not from Monzo.
    [Fact]
    public async Task Rejects_joint_on_a_transaction_not_from_monzo()
    {
        await Given.SeededAsync();
        var response = await Patch($"/api/transactions/{Arrange.RentTransactionId}", new { owner = "Joint" });
        await Verify(response);
    }

    [Fact]
    public async Task Allows_joint_on_a_monzo_transaction()
    {
        await Given.SeededAsync();
        var response = await Patch($"/api/transactions/{Arrange.SettlementTransactionId}", new { owner = "Joint" });
        await Verify(response);
    }

    /// Groceries starts in Needs. The seeded bucket rule sends category Rent to
    /// Wants, so moving it to Rent must move its bucket with it.
    [Fact]
    public async Task Reruns_bucket_rules_when_the_category_changes()
    {
        await Given.SeededWithRulesAsync();
        var response = await Patch($"/api/transactions/{Arrange.GroceriesTransactionId}",
            new { categoryId = Arrange.RentCategoryId });
        await Verify(response);
    }

    [Fact]
    public async Task Keeps_a_bucket_sent_alongside_the_category()
    {
        await Given.SeededWithRulesAsync();
        var response = await Patch($"/api/transactions/{Arrange.GroceriesTransactionId}",
            new { categoryId = Arrange.RentCategoryId, bucket = "Savings" });
        await Verify(response);
    }
}

public class DeleteTransactionTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Deletes_a_transaction()
    {
        await Given.SeededAsync();
        var response = await Delete($"/api/transactions/{Arrange.RentTransactionId}");
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_transaction()
    {
        await Given.SeededAsync();
        var response = await Delete("/api/transactions/nope");
        await Verify(response);
    }
}
