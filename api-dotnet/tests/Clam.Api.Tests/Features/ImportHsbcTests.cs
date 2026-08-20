namespace Clam.Api.Tests.Features;

/// The HSBC upload endpoint, over the wire, against real statements.
public class ImportHsbcTests(ClamApiFactory api) : ApiTest(api)
{
    private const string April = "2026-04-09-hsbc-statement.pdf";
    private const string May = "2026-05-09-hsbc-statement.pdf";

    private Task<HttpResponseMessage> Upload(string fileName, string owner = "Joint")
        => PostFile("/api/admin/import/hsbc", Statement(fileName), fileName,
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
        var response = await Upload(April);
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_the_identical_file_twice()
    {
        await Given.NothingAsync();
        await Upload(April);

        var response = await Upload(April);
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_a_statement_whose_rows_are_all_already_staged()
    {
        await Given.NothingAsync();
        await Upload(April);

        var response = await PostFile("/api/admin/import/hsbc", Rerendered(April), "re-downloaded.pdf");
        await Verify(response);
    }

    /// Consecutive statements overlap at the boundary — May opens with the
    /// balance April closed on — so this proves the ids key on content and not
    /// on position in the file.
    [Fact]
    public async Task Stages_a_second_statement_alongside_the_first()
    {
        await Given.NothingAsync();
        await Upload(April);

        var response = await Upload(May);
        await Verify(response);
    }

    /// An Amex statement uploaded to the HSBC card. It parses fine and is simply
    /// the wrong bank, which is a 422 naming the bank it actually found.
    [Fact]
    public async Task Refuses_another_banks_statement()
    {
        await Given.NothingAsync();

        var response = await PostFile("/api/admin/import/hsbc",
            Statement("2026-01-24-amex.pdf"), "amex.pdf");
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_a_request_with_no_file()
    {
        await Given.NothingAsync();

        var response = await PostFile("/api/admin/import/hsbc", [], "empty.pdf");
        await Verify(response);
    }

    /// Upload then process, end to end, so the direction the parser read off the
    /// column survives all the way into [Transactions] as Income vs Expense.
    [Fact]
    public async Task Normalises_staged_rows_into_transactions()
    {
        await Given.SeededWithUncategorisedAsync();
        await Upload(April);

        var response = await Post("/api/admin/process");
        await Verify(response);
    }
}
