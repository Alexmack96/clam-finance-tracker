namespace Clam.Api.Tests.Features;

/// Every user-writable string column in `db/schema.sql` is bounded, so each one
/// needs a `MaximumLength` in front of it. Without one the request is well-formed,
/// binds cleanly, passes validation and dies in SQL Server with "String or binary
/// data would be truncated" — a 500 for what is squarely the caller's mistake.
///
/// One test per bounded column that a public endpoint writes, each sending
/// exactly one character more than the column holds. The numbers here are not
/// arbitrary: they are the column widths, and a schema change that narrows a
/// column without narrowing its validator fails here rather than in production.
///
/// Columns already covered by a stricter rule are not repeated: `Categories.name`
/// is NVARCHAR(100) but capped at 40, `color` is NVARCHAR(20) but must match a
/// 7-character hex pattern, and `RuleConditions.value` and the two
/// `RecurringVerdicts` columns already match their widths exactly.
public class LengthLimitTests(ClamApiFactory api) : ApiTest(api)
{
    /// Notes.title is NVARCHAR(300).
    [Fact]
    public async Task Create_note_rejects_a_title_longer_than_the_column()
    {
        await Given.NothingAsync();
        var response = await Post("/api/notes", new { title = new string('t', 301) });
        await Verify(response);
    }

    [Fact]
    public async Task Update_note_rejects_a_title_longer_than_the_column()
    {
        await Given.NotesAsync();
        var response = await Patch($"/api/notes/{Arrange.PlainNoteId}", new { title = new string('t', 301) });
        await Verify(response);
    }

    /// Tabs.person is NVARCHAR(200), Tabs.description is NVARCHAR(500).
    [Fact]
    public async Task Create_tab_rejects_a_person_and_description_longer_than_their_columns()
    {
        await Given.NothingAsync();
        var response = await Post("/api/tabs", new
        {
            person = new string('p', 201),
            description = new string('d', 501),
            amount = 5,
            direction = "IOwe",
        });
        await Verify(response);
    }

    [Fact]
    public async Task Update_tab_rejects_a_person_and_description_longer_than_their_columns()
    {
        await Given.TabsAsync();
        var response = await Patch($"/api/tabs/{Arrange.OpenTabId}", new
        {
            person = new string('p', 201),
            description = new string('d', 501),
        });
        await Verify(response);
    }

    /// InvestmentAccounts.name is NVARCHAR(200).
    [Fact]
    public async Task Create_investment_account_rejects_a_name_longer_than_the_column()
    {
        await Given.NothingAsync();
        var response = await Post("/api/investments/accounts",
            new { name = new string('n', 201), category = "cash", owner = "Alex" });
        await Verify(response);
    }

    [Fact]
    public async Task Update_investment_account_rejects_a_name_longer_than_the_column()
    {
        await Given.InvestmentHistoryAsync();
        var response = await Patch("/api/investments/accounts/cacct0000000000001equity",
            new { name = new string('n', 201) });
        await Verify(response);
    }

    /// Transactions.categoryId is NVARCHAR(30). Not a length the client would
    /// ever hit by accident, but the UPDATE COALESCEs it straight into the column
    /// and a truncation error there is indistinguishable from a real failure.
    [Fact]
    public async Task Update_transaction_rejects_a_category_id_longer_than_the_column()
    {
        await Given.SeededAsync();
        var response = await Patch($"/api/transactions/{Arrange.RentTransactionId}",
            new { categoryId = new string('c', 31) });
        await Verify(response);
    }

    /// The other half of the boundary: exactly at the limit must still succeed.
    /// An off-by-one in a MaximumLength is otherwise invisible — every test above
    /// passes just as well against a rule that is one character too strict.
    [Fact]
    public async Task A_title_of_exactly_the_column_width_is_accepted()
    {
        await Given.NothingAsync();
        var response = await Post("/api/notes", new { title = new string('t', 300) });
        await Verify(response);
    }

    [Fact]
    public async Task A_person_and_description_of_exactly_the_column_width_are_accepted()
    {
        await Given.NothingAsync();
        var response = await Post("/api/tabs", new
        {
            person = new string('p', 200),
            description = new string('d', 500),
            amount = 5,
            direction = "IOwe",
        });
        await Verify(response);
    }
}
