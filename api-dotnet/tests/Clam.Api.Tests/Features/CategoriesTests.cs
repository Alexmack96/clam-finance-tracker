namespace Clam.Api.Tests.Features;

public class GetCategoriesTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Lists_every_category_with_its_usage_count()
    {
        await Given.SeededAsync();
        var response = await Get("/api/categories");
        await Verify(response);
    }

    [Fact]
    public async Task Lists_nothing_when_there_are_no_categories()
    {
        await Given.NothingAsync();
        var response = await Get("/api/categories");
        await Verify(response);
    }
}

public class CreateCategoryTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Creates_a_category()
    {
        await Given.SeededAsync();
        var response = await Post("/api/categories", new { name = "Coffee", color = "#14b8a6" });
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_a_name_that_is_already_taken()
    {
        await Given.SeededAsync();
        var response = await Post("/api/categories", new { name = "Rent", color = "#ffffff" });
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_a_colour_that_is_not_hex()
    {
        await Given.SeededAsync();
        var response = await Post("/api/categories", new { name = "Bad", color = "teal" });
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_a_name_that_is_too_long()
    {
        await Given.SeededAsync();
        var response = await Post("/api/categories", new { name = new string('x', 41), color = "#14b8a6" });
        await Verify(response);
    }
}

public class UpdateCategoryTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Renames_a_category_and_leaves_its_colour_alone()
    {
        await Given.SeededAsync();
        var response = await Patch($"/api/categories/{Arrange.GroceriesCategoryId}", new { name = "Food" });
        await Verify(response);
    }

    [Fact]
    public async Task Recolours_a_category_and_leaves_its_name_alone()
    {
        await Given.SeededAsync();
        var response = await Patch($"/api/categories/{Arrange.GroceriesCategoryId}", new { color = "#111111" });
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_a_rename_onto_an_existing_name()
    {
        await Given.SeededAsync();
        var response = await Patch($"/api/categories/{Arrange.GroceriesCategoryId}", new { name = "Rent" });
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_category()
    {
        await Given.SeededAsync();
        var response = await Patch("/api/categories/nope", new { name = "Whatever" });
        await Verify(response);
    }
}

public class DeleteCategoryTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Deletes_a_category_nothing_uses()
    {
        await Given.SeededAsync();
        var response = await Delete($"/api/categories/{Arrange.UnusedCategoryId}");
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_a_category_that_still_has_transactions()
    {
        await Given.SeededAsync();
        var response = await Delete($"/api/categories/{Arrange.RentCategoryId}");
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_to_delete_uncategorised()
    {
        await Given.SeededWithUncategorisedAsync();
        var response = await Delete($"/api/categories/{Arrange.UncategorisedCategoryId}");
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_category()
    {
        await Given.SeededAsync();
        var response = await Delete("/api/categories/nope");
        await Verify(response);
    }
}

public class MergeCategoriesTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Moves_transactions_and_reports_what_it_moved()
    {
        await Given.SeededWithRulesAsync();
        var response = await Post($"/api/categories/{Arrange.GroceriesCategoryId}/merge/{Arrange.RentCategoryId}");
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_to_merge_a_category_into_itself()
    {
        await Given.SeededAsync();
        var response = await Post($"/api/categories/{Arrange.RentCategoryId}/merge/{Arrange.RentCategoryId}");
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_to_merge_uncategorised_away()
    {
        await Given.SeededWithUncategorisedAsync();
        var response = await Post($"/api/categories/{Arrange.UncategorisedCategoryId}/merge/{Arrange.RentCategoryId}");
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_when_the_source_does_not_exist()
    {
        await Given.SeededAsync();
        var response = await Post($"/api/categories/nope/merge/{Arrange.RentCategoryId}");
        await Verify(response);
    }
}
