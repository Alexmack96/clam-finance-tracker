namespace Clam.Api.Tests.Features;

/// The upload endpoint, over the wire, against real statements.
public class ImportAmexTests(ClamApiFactory api) : ApiTest(api)
{
    private const string January = "2026-01-24-amex.pdf";
    private const string February = "2026-02-24-amex.pdf";

    private Task<HttpResponseMessage> Upload(string fileName, string owner = "Alex")
        => PostFile("/api/admin/import/amex", Statement(fileName), fileName,
            fields: new Dictionary<string, string> { ["owner"] = owner });

    [Fact]
    public async Task Stages_every_row_of_a_statement()
    {
        await Given.NothingAsync();
        var response = await Upload(January);
        await Verify(response);
    }

    /// The cheapest rejection there is, and the one that has to come first:
    /// caught on the file's hash, before parsing, so it cannot depend on how row
    /// ids happen to be derived.
    [Fact]
    public async Task Refuses_the_identical_file_twice()
    {
        await Given.NothingAsync();
        await Upload(January);

        var response = await Upload(January);
        await Verify(response);
    }

    /// The same statement arriving as *different bytes* — re-downloaded, or
    /// re-rendered by the bank — sails past the content hash and has to be
    /// caught on its rows instead. This is the rejection that has to happen
    /// before anything is written, or a statement adding no rows still leaves a
    /// statement row and a PDF on the volume behind it.
    [Fact]
    public async Task Refuses_a_statement_whose_rows_are_all_already_staged()
    {
        await Given.NothingAsync();
        await Upload(January);

        var response = await PostFile("/api/admin/import/amex", Rerendered(January), "re-downloaded.pdf",
            fields: new Dictionary<string, string> { ["owner"] = "Alex" });
        await Verify(response);
    }

    /// Every bank endpoint accepts any PDF, so uploading one bank's statement
    /// under another's card is a mistake waiting to happen. The guard turns it
    /// into a message naming the bank it actually found.
    ///
    /// A real HSBC statement, not a synthetic one: the point of the check is
    /// that the document parses fine and is simply the wrong bank, which is a
    /// 422. Corrupt bytes fail earlier and prove nothing about the guard.
    [Fact]
    public async Task Refuses_another_banks_statement()
    {
        await Given.NothingAsync();

        var response = await PostFile("/api/admin/import/amex",
            Statement("2026-04-09-hsbc-statement.pdf"), "hsbc.pdf");
        await Verify(response);
    }

    /// Byte-different, content-identical: a PDF tolerates trailing bytes after
    /// its %%EOF, so this is the smallest honest way to produce the "same
    /// statement, new file" case without keeping a second copy of a statement
    /// around purely to be re-rendered.
    private static byte[] Rerendered(string fileName) =>
        [.. Statement(fileName), .. "\n%% re-rendered\n"u8];

    [Fact]
    public async Task Refuses_a_request_with_no_file()
    {
        await Given.NothingAsync();

        var response = await PostFile("/api/admin/import/amex", [], "empty.pdf");
        await Verify(response);
    }

    /// Two different statements share no rows, so the second is a clean import
    /// rather than a partial duplicate — the case that proves the id scheme is
    /// keyed on content and not on position in the file.
    [Fact]
    public async Task Stages_a_second_statement_alongside_the_first()
    {
        await Given.NothingAsync();
        await Upload(January);

        var response = await Upload(February);
        await Verify(response);
    }

    /// Owner is part of the business key, so the *rows* of a statement are
    /// per-person — but the content hash is not, and it is checked first. Re-
    /// uploading one person's actual file under the other's name is therefore
    /// refused, and that is the intended order: the same document is the same
    /// document, and each person's statement is a different document.
    ///
    /// Pinned as a test because the two rules disagree on this input, and which
    /// one wins is the kind of thing a later refactor could silently swap.
    [Fact]
    public async Task Refuses_the_same_file_uploaded_under_a_different_owner()
    {
        await Given.NothingAsync();
        await Upload(January);

        var response = await PostFile("/api/admin/import/amex", Statement(January), "caseys-copy.pdf",
            fields: new Dictionary<string, string> { ["owner"] = "Casey" });
        await Verify(response);
    }

    /// Casey's own statement, which is a different document, does import — and
    /// imports every row, because the owner is in the key and none of Alex's
    /// ids collide with hers.
    [Fact]
    public async Task Stages_a_different_file_for_a_different_owner()
    {
        await Given.NothingAsync();
        await Upload(January);

        var response = await PostFile("/api/admin/import/amex", Rerendered(January), "caseys-statement.pdf",
            fields: new Dictionary<string, string> { ["owner"] = "Casey" });
        await Verify(response);
    }
}

/// The foreign-currency half of a staged row, once it reaches [Transactions].
public class AmexForeignCurrencyTests(ClamApiFactory api) : ApiTest(api)
{
    /// Amex converts the charge itself and prints the sterling it took, so
    /// [amount] is that figure and the merchant's own is recorded beside it.
    /// Nothing is re-converted — an FX lookup here would disagree with the
    /// statement, which is the document of record.
    [Fact]
    public async Task Records_the_foreign_amount_without_reconverting_it()
    {
        await Given.SeededWithUncategorisedAsync();
        await Given.StagedForeignAmexRowAsync();
        await Post("/api/admin/process");

        var response = await Get("/api/transactions?owner=Alex");
        await Verify(response);
    }

    /// The printed name is mapped to an ISO code because that is what the column
    /// holds. A name this build does not know produces neither half rather than
    /// a guess — the staged row still has the statement's own wording, so
    /// nothing is lost, and a wrong code would be worse than a missing one.
    [Fact]
    public async Task Leaves_an_unrecognised_currency_name_unconverted()
    {
        await Given.SeededWithUncategorisedAsync();
        await Given.StagedForeignAmexRowAsync(foreignCurrency: "ATLANTEAN DOUBLOON");
        await Post("/api/admin/process");

        var response = await Get("/api/transactions?owner=Alex");
        await Verify(response);
    }
}
