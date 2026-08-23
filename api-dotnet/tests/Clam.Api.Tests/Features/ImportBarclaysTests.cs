namespace Clam.Api.Tests.Features;

/// The Barclaycard upload endpoint, over the wire, against real statements.
public class ImportBarclaysTests(ClamApiFactory api) : ApiTest(api)
{
    /// Barclays' own download names, kept verbatim — double space and all — so
    /// the fixture is provably the document rather than a tidied copy of it.
    private const string January = "Monthly BarclayCard Statement_26-JAN-26  270310481775250939803.pdf";
    private const string February = "Monthly BarclayCard Statement_24-FEB-26  250354261775250950657.pdf";

    private Task<HttpResponseMessage> Upload(string fileName, string owner = "Alex")
        => PostFile("/api/admin/import/barclays", Statement(fileName), fileName,
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

        var response = await PostFile("/api/admin/import/barclays", Rerendered(January), "re-downloaded.pdf");
        await Verify(response);
    }

    /// Two consecutive statements, which is where a positional bug shows up: the
    /// February statement repeats January's closing balance as its opening one,
    /// so ids that keyed on anything but row content would collide here.
    [Fact]
    public async Task Stages_a_second_statement_alongside_the_first()
    {
        await Given.NothingAsync();
        await Upload(January);

        var response = await Upload(February);
        await Verify(response);
    }

    /// An Amex statement uploaded to the Barclaycard card. It parses fine and is
    /// simply the wrong bank, which is a 422 naming the bank it actually found.
    [Fact]
    public async Task Refuses_another_banks_statement()
    {
        await Given.NothingAsync();

        var response = await PostFile("/api/admin/import/barclays",
            Statement("2026-01-24-amex.pdf"), "amex.pdf");
        await Verify(response);
    }

    [Fact]
    public async Task Refuses_a_request_with_no_file()
    {
        await Given.NothingAsync();

        var response = await PostFile("/api/admin/import/barclays", [], "empty.pdf");
        await Verify(response);
    }

    /// Upload then process, end to end. The credits are the interesting half:
    /// a payment towards this card comes out of an account that is imported
    /// separately, so booking it would count the same money twice — the process
    /// step skips them rather than making them Income.
    [Fact]
    public async Task Normalises_staged_rows_into_transactions()
    {
        await Given.SeededWithUncategorisedAsync();
        await Upload(January);

        var response = await Post("/api/admin/process");
        await Verify(response);
    }
}
