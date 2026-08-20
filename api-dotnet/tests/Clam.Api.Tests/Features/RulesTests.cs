namespace Clam.Api.Tests.Features;

/// The rule body every test here sends: route TESCO to Rent, which is a change
/// the seeded Groceries row makes visible.
internal static class RuleBodies
{
    internal static object TescoToRent(string categoryId) => new
    {
        kind = "Category",
        joinOperator = "AND",
        categoryId,
        conditions = new[] { new { field = "Description", @operator = "Contains", value = "TESCO", negate = false } },
    };

    internal static object OnlyExclusions(string categoryId) => new
    {
        kind = "Category",
        categoryId,
        conditions = new[] { new { field = "Description", @operator = "Contains", value = "TESCO", negate = true } },
    };
}

public class GetRulesTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Lists_rules_with_their_conditions_nested()
    {
        await Given.SeededWithRulesAsync();
        var response = await Get("/api/rules");
        await Verify(response);
    }

    [Fact]
    public async Task Lists_nothing_when_no_rules_exist()
    {
        await Given.SeededAsync();
        var response = await Get("/api/rules");
        await Verify(response);
    }
}

public class CreateRuleTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Creates_a_rule_at_the_bottom_of_its_kind()
    {
        await Given.SeededWithRulesAsync();
        var response = await Post("/api/rules", RuleBodies.TescoToRent(Arrange.RentCategoryId));
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_a_category_rule_with_no_category()
    {
        await Given.SeededAsync();
        var response = await Post("/api/rules", new
        {
            kind = "Category",
            conditions = new[] { new { field = "Description", @operator = "Contains", value = "X", negate = false } },
        });
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_a_rule_of_nothing_but_exclusions()
    {
        await Given.SeededAsync();
        var response = await Post("/api/rules", RuleBodies.OnlyExclusions(Arrange.RentCategoryId));
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_a_rule_with_no_conditions()
    {
        await Given.SeededAsync();
        var response = await Post("/api/rules",
            new { kind = "Category", categoryId = Arrange.RentCategoryId, conditions = Array.Empty<object>() });
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_a_bank_it_does_not_recognise()
    {
        await Given.SeededAsync();
        var response = await Post("/api/rules", new
        {
            kind = "Category",
            categoryId = Arrange.RentCategoryId,
            bank = "starling",
            conditions = new[] { new { field = "Description", @operator = "Contains", value = "X", negate = false } },
        });
        await Verify(response);
    }
}

public class UpdateRuleTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Replaces_the_condition_set_wholesale_without_moving_the_rule()
    {
        await Given.SeededWithRulesAsync();
        var response = await Patch("/api/rules/crule0000000000000001cat", new
        {
            kind = "Category",
            joinOperator = "OR",
            categoryId = Arrange.RentCategoryId,
            conditions = new[]
            {
                new { field = "Description", @operator = "Contains", value = "ALDI", negate = false },
                new { field = "Description", @operator = "Contains", value = "LIDL", negate = false },
            },
        });
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_rule()
    {
        await Given.SeededAsync();
        var response = await Patch("/api/rules/nope", RuleBodies.TescoToRent(Arrange.RentCategoryId));
        await Verify(response);
    }
}

public class ReorderRulesTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Rejects_a_list_that_omits_a_rule()
    {
        await Given.SeededWithRulesAsync();
        var response = await Post("/api/rules/reorder", new { kind = "Category", ids = Array.Empty<string>() });
        await Verify(response);
    }

    [Fact]
    public async Task Returns_the_rules_of_that_kind_in_their_new_order()
    {
        await Given.SeededWithRulesAsync();
        var response = await Post("/api/rules/reorder",
            new { kind = "Category", ids = new[] { "crule0000000000000001cat" } });
        await Verify(response);
    }
}

public class DeleteRuleTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Deletes_a_rule()
    {
        await Given.SeededWithRulesAsync();
        var response = await Delete("/api/rules/crule0000000000000001cat");
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_rule()
    {
        await Given.SeededAsync();
        var response = await Delete("/api/rules/nope");
        await Verify(response);
    }
}

public class PreviewRulesTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Reports_what_the_whole_saved_set_would_change()
    {
        await Given.SeededWithRulesAsync();
        var response = await Post("/api/rules/preview", new { scope = "all" });
        await Verify(response);
    }

    [Fact]
    public async Task Reports_matched_and_won_for_a_single_rule()
    {
        await Given.SeededWithRulesAsync();
        var response = await Post("/api/rules/preview", new { scope = "rule", ruleId = "crule0000000000000001cat" });
        await Verify(response);
    }

    [Fact]
    public async Task Previews_a_draft_without_saving_it()
    {
        await Given.SeededWithUncategorisedAsync();
        var response = await Post("/api/rules/preview",
            new { scope = "draft", rule = RuleBodies.TescoToRent(Arrange.RentCategoryId) });
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_rule()
    {
        await Given.SeededAsync();
        var response = await Post("/api/rules/preview", new { scope = "rule", ruleId = "nope" });
        await Verify(response);
    }
}

public class ApplyRulesTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Writes_the_plan_and_reports_the_counts()
    {
        await Given.SeededWithRulesAsync();
        var response = await Post("/api/rules/apply", new { scope = "all" });
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_to_apply_a_draft()
    {
        await Given.SeededWithRulesAsync();
        var response = await Post("/api/rules/apply", new { scope = "draft" });
        await Verify(response);
    }
}
