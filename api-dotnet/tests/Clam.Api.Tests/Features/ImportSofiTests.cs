namespace Clam.Api.Tests.Features;

/// The SoFi upload endpoint, over the wire, against real statements.
public class ImportSofiTests(ClamApiFactory api) : ApiTest(api)
{
    private const string February = "SoFiMoneyStatement_2026-02-28.pdf";
    private const string March = "SoFiMoneyStatement_2026-03-31.pdf";

    private Task<HttpResponseMessage> Upload(string fileName, string owner = "Casey")
        => PostFile("/api/admin/import/sofi", Statement(fileName), fileName,
            fields: new Dictionary<string, string> { ["owner"] = owner });

    /// Byte-different, content-identical: a PDF tolerates trailing bytes after
    /// its %%EOF, which is the smallest honest way to produce the "same
    /// statement, re-downloaded" case.
    private static byte[] Rerendered(string fileName) =>
        [.. Statement(fileName), .. "\n%% re-rendered\n"u8];

    /// Both accounts' rows land against the one statement file, because it is
    /// one document.
    [Fact]
    public async Task Stages_every_row_of_a_statement()
    {
        await Given.NothingAsync();
        var response = await Upload(February);
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_the_identical_file_twice()
    {
        await Given.NothingAsync();
        await Upload(February);

        var response = await Upload(February);
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_a_statement_whose_rows_are_all_already_staged()
    {
        await Given.NothingAsync();
        await Upload(February);

        var response = await PostFile("/api/admin/import/sofi", Rerendered(February), "re-downloaded.pdf");
        await Verify(response);
    }

    [Fact]
    public async Task Stages_a_second_statement_alongside_the_first()
    {
        await Given.NothingAsync();
        await Upload(February);

        var response = await Upload(March);
        await Verify(response);
    }

    /// An Amex statement uploaded to the SoFi account. It parses fine and is
    /// simply the wrong bank, which is a 422 naming the bank it actually found.
    [Fact]
    public async Task Refuses_another_banks_statement()
    {
        await Given.NothingAsync();

        var response = await PostFile("/api/admin/import/sofi",
            Statement("2026-01-24-amex.pdf"), "amex.pdf");
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_a_request_with_no_file()
    {
        await Given.NothingAsync();

        var response = await PostFile("/api/admin/import/sofi", [], "empty.pdf");
        await Verify(response);
    }

    /// Upload then process, end to end. The two sides of the Checking/Savings
    /// transfer are skipped rather than booked — they are the same dollars
    /// moving between the account holder's own accounts, and importing either
    /// would count money that was never spent.
    [Fact]
    public async Task Normalises_staged_rows_into_transactions()
    {
        await Given.SeededWithUncategorisedAsync();
        await Upload(February);

        var response = await Post("/api/admin/process");
        await Verify(response);
    }
}
