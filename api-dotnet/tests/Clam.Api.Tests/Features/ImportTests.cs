namespace Clam.Api.Tests.Features;

public class GetStagedCountsTests(ClamApiFactory api) : ApiTest(api)
{
    private async Task AnAmexRowIsStaged()
    {
        await Given.SeededWithUncategorisedAsync();
        await Given.StagedAmexRowAsync();
    }

    [Fact]
    public async Task Counts_pending_rows_and_breaks_them_down_by_owner()
    {
        await AnAmexRowIsStaged();
        var response = await Get("/api/admin/staged");
        await Verify(response);
    }

    [Fact]
    public async Task Counts_zero_everywhere_when_nothing_is_staged()
    {
        await Given.NothingAsync();
        var response = await Get("/api/admin/staged");
        await Verify(response);
    }
}

public class GetLastStatementTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Reports_the_newest_imported_date_per_bank()
    {
        await Given.SeededAsync();
        var response = await Get("/api/admin/last-statement");
        await Verify(response);
    }

    [Fact]
    public async Task Reports_null_for_every_bank_when_nothing_is_imported()
    {
        await Given.NothingAsync();
        var response = await Get("/api/admin/last-statement");
        await Verify(response);
    }
}

public class ProcessStagedTests(ClamApiFactory api) : ApiTest(api)
{
    private async Task AnAmexRowIsStaged(string id = "amex_row_1", string amount = "12.34")
    {
        await Given.SeededWithUncategorisedAsync();
        await Given.StagedAmexRowAsync(id, amount);
    }

    /// Staged *and* already processed once, which is the state the idempotency
    /// and rule-application assertions are actually about.
    private async Task AnAmexRowHasBeenProcessed()
    {
        await AnAmexRowIsStaged();
        await Post("/api/admin/process");
    }

    private async Task AnAmexRowIsStagedWithRules()
    {
        await Given.SeededWithRulesAsync();
        await Given.StagedAmexRowAsync();
        await Post("/api/admin/process");
    }

    [Fact]
    public async Task Normalises_a_staged_amex_row_into_a_transaction()
    {
        await AnAmexRowIsStaged();
        var response = await Post("/api/admin/process");
        await Verify(response);
    }

    [Fact]
    public async Task Applies_the_rules_it_would_dry_run()
    {
        await AnAmexRowIsStagedWithRules();
        var response = await Get("/api/transactions?owner=Alex");
        await Verify(response);
    }

    [Fact]
    public async Task Errors_only_the_row_whose_amount_will_not_parse()
    {
        await AnAmexRowIsStaged("amex_bad", "not a number");
        var response = await Post("/api/admin/process");
        await Verify(response);
    }

    [Fact]
    public async Task Skips_a_barclays_credit_and_imports_the_debit()
    {
        await Given.SeededWithUncategorisedAsync();
        await Given.StagedBarclaysPairAsync();
        await Verify(await Post("/api/admin/process"));
    }

    [Fact]
    public async Task Converts_a_dollar_row_to_sterling()
    {
        await Given.SeededWithUncategorisedAsync();
        await Given.StagedSofiRowAsync();
        await Verify(await Post("/api/admin/process"));
    }

    [Fact]
    public async Task Does_nothing_on_a_second_run()
    {
        await AnAmexRowHasBeenProcessed();
        var response = await Post("/api/admin/process");
        await Verify(response);
    }
}

public class BackfillUsdGbpTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Finds_no_candidates_in_sterling_only_data()
    {
        await Given.SeededAsync();
        var response = await Post("/api/admin/backfill/usd-gbp", new { dryRun = true });
        await Verify(response);
    }
}
