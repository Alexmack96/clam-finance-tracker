namespace Clam.Api.Tests.Features;

public class GetRecurringTests(ClamApiFactory api) : ApiTest(api)
{
    private async Task AMonthlySubscriptionExists()
    {
        await Given.SeededAsync();
        await Given.MonthlySubscriptionAsync();
    }

    [Fact]
    public async Task Detects_a_monthly_series_and_leaves_it_proposed()
    {
        await AMonthlySubscriptionExists();
        var response = await Get("/api/recurring?owner=Alex");
        await Verify(response);
    }

    [Fact]
    public async Task Detects_nothing_from_transactions_that_do_not_repeat()
    {
        await Given.SeededAsync();
        var response = await Get("/api/recurring?owner=Alex");
        await Verify(response);
    }

    [Fact]
    public async Task Falls_back_to_alex_for_an_owner_it_does_not_recognise()
    {
        await Given.SeededAsync();
        var response = await Get("/api/recurring?owner=Nobody");
        await Verify(response);
    }
}

public class SetRecurringVerdictTests(ClamApiFactory api) : ApiTest(api)
{
    private const string Description = "NETFLIX.COM";

    private async Task AConfirmedMonthlySubscriptionExists()
    {
        await Given.SeededAsync();
        await Given.MonthlySubscriptionAsync(Description);
        await Put("/api/recurring/verdict", new { owner = "Alex", description = Description, status = "Confirmed" });
    }

    [Fact]
    public async Task Records_a_confirmation()
    {
        await Given.SeededAsync();
        var response = await Put("/api/recurring/verdict",
            new { owner = "Alex", description = Description, status = "Confirmed" });
        await Verify(response);
    }

    [Fact]
    public async Task Counts_a_confirmed_series_toward_the_committed_total()
    {
        await AConfirmedMonthlySubscriptionExists();
        var response = await Get("/api/recurring?owner=Alex");
        await Verify(response);
    }

    [Fact]
    public async Task Clears_the_verdict_when_the_status_is_null()
    {
        await AConfirmedMonthlySubscriptionExists();
        var response = await Put("/api/recurring/verdict",
            new { owner = "Alex", description = Description, status = (string?)null });
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_joint_as_a_recurring_owner()
    {
        await Given.SeededAsync();
        var response = await Put("/api/recurring/verdict",
            new { owner = "Joint", description = Description, status = "Confirmed" });
        await Verify(response);
    }
}

public class UpdateRecurringNoteTests(ClamApiFactory api) : ApiTest(api)
{
    private const string Description = "NETFLIX.COM";

    private async Task ARejectedVerdictExists()
    {
        await Given.SeededAsync();
        await Put("/api/recurring/verdict", new { owner = "Alex", description = Description, status = "Rejected" });
    }

    [Fact]
    public async Task Adds_a_note_to_an_existing_verdict()
    {
        await ARejectedVerdictExists();
        var response = await Patch("/api/recurring/note",
            new { owner = "Alex", description = Description, note = "cancelled 3 Aug" });
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_a_note_on_a_series_with_no_verdict()
    {
        await Given.SeededAsync();
        var response = await Patch("/api/recurring/note",
            new { owner = "Alex", description = Description, note = "cancelled 3 Aug" });
        await Verify(response);
    }
}
