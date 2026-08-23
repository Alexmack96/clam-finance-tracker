namespace Clam.Api.Tests.Features;

/// The Santander upload endpoint, over the wire, against real statements.
public class ImportSantanderTests(ClamApiFactory api) : ApiTest(api)
{
    private const string January = "2026-21-01-to-2026-02-20-santander.pdf";
    private const string February = "2026-21-02-to-2026-20-03-santander.pdf";

    private Task<HttpResponseMessage> Upload(string fileName, string owner = "Alex")
        => PostFile("/api/admin/import/santander", Statement(fileName), fileName,
            fields: new Dictionary<string, string> { ["owner"] = owner });

    /// Byte-different, content-identical: a PDF tolerates trailing bytes after
    /// its %%EOF, which is the smallest honest way to produce the "same
    /// statement, re-downloaded" case.
    private static byte[] Rerendered(string fileName) =>
        [.. Statement(fileName), .. "\n%% re-rendered\n"u8];

    [Fact]
    public async Task Stages_every_row_of_a_statement()
    {
        await Given.NothingAsync();
        var response = await Upload(January);
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_the_identical_file_twice()
    {
        await Given.NothingAsync();
        await Upload(January);

        var response = await Upload(January);
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_a_statement_whose_rows_are_all_already_staged()
    {
        await Given.NothingAsync();
        await Upload(January);

        var response = await PostFile("/api/admin/import/santander", Rerendered(January), "re-downloaded.pdf");
        await Verify(response);
    }

    /// Consecutive statements abut rather than overlap — February opens the day
    /// after January closed — so every row of the second is new.
    [Fact]
    public async Task Stages_a_second_statement_alongside_the_first()
    {
        await Given.NothingAsync();
        await Upload(January);

        var response = await Upload(February);
        await Verify(response);
    }

    /// An Amex statement uploaded to the Santander account. It parses fine and is
    /// simply the wrong bank, which is a 422 naming the bank it actually found.
    [Fact]
    public async Task Refuses_another_banks_statement()
    {
        await Given.NothingAsync();

        var response = await PostFile("/api/admin/import/santander",
            Statement("2026-01-24-amex.pdf"), "amex.pdf");
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_a_request_with_no_file()
    {
        await Given.NothingAsync();

        var response = await PostFile("/api/admin/import/santander", [], "empty.pdf");
        await Verify(response);
    }

    /// Upload then process, end to end, so the direction the parser read off the
    /// column survives all the way into [Transactions] as Income vs Expense.
    [Fact]
    public async Task Normalises_staged_rows_into_transactions()
    {
        await Given.SeededWithUncategorisedAsync();
        await Upload(January);

        var response = await Post("/api/admin/process");
        await Verify(response);
    }
}
