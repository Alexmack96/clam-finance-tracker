namespace Clam.Api.Tests.Features;

/// The Chase upload endpoint, over the wire, against real statements.
public class ImportChaseTests(ClamApiFactory api) : ApiTest(api)
{
    private const string February = "2026-02-22-chase.pdf";
    private const string March = "2026-03-22-chase.pdf";

    /// The one with a page of transactions flattened to an image. It is a
    /// fixture for the refusal, not for reading rows.
    private const string January = "2026-01-22-chase.pdf";

    private Task<HttpResponseMessage> Upload(string fileName, string owner = "Casey")
        => PostFile("/api/admin/import/chase", Statement(fileName), fileName,
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

        var response = await PostFile("/api/admin/import/chase", Rerendered(February), "re-downloaded.pdf");
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

    /// Nothing is stored when the parse is refused — not the PDF, not a
    /// StatementFiles row. This is the case that proves it, because the refusal
    /// comes from the reconciliation rather than from the extractor: the file
    /// read perfectly and simply does not add up.
    [Fact]
    public async Task Stores_nothing_when_the_statement_cannot_be_reconciled()
    {
        await Given.NothingAsync();

        var upload = await Upload(January);
        var statements = await Get("/api/admin/statements");

        await Verify(new { upload, statements });
    }

    /// An Amex statement uploaded to the Chase card. It parses fine and is
    /// simply the wrong bank, which is a 422 naming the bank it actually found.
    [Fact]
    public async Task Refuses_another_banks_statement()
    {
        await Given.NothingAsync();

        var response = await PostFile("/api/admin/import/chase",
            Statement("2026-01-24-amex.pdf"), "amex.pdf");
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_a_request_with_no_file()
    {
        await Given.NothingAsync();

        var response = await PostFile("/api/admin/import/chase", [], "empty.pdf");
        await Verify(response);
    }

    /// Upload then process, end to end. Chase stages dollars, so this is also
    /// the FX path: the process step converts each row to sterling and keeps the
    /// dollars on originalAmount.
    [Fact]
    public async Task Normalises_staged_rows_into_transactions()
    {
        await Given.SeededWithUncategorisedAsync();
        await Upload(February);

        var response = await Post("/api/admin/process");
        await Verify(response);
    }
}
