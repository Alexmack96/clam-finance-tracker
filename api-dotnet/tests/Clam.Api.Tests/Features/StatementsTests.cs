namespace Clam.Api.Tests.Features;

public class GetStatementsTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Lists_statements_with_their_staged_row_counts()
    {
        await Given.StatementWithRowsAsync();
        var response = await Get("/api/admin/statements");
        await Verify(response);
    }

    [Fact]
    public async Task Lists_nothing_when_no_statement_has_been_uploaded()
    {
        await Given.NothingAsync();
        var response = await Get("/api/admin/statements");
        await Verify(response);
    }

    /// The count is over every staging table, not Amex's. Counting one table
    /// reports an uploaded HSBC statement as having produced nothing, which
    /// reads exactly like a parse that silently dropped every row.
    [Fact]
    public async Task Counts_the_staged_rows_of_a_bank_that_is_not_Amex()
    {
        await Given.NothingAsync();
        await PostFile("/api/admin/import/hsbc",
            Statement("2026-04-09-hsbc-statement.pdf"), "2026-04-09-hsbc-statement.pdf",
            fields: new Dictionary<string, string> { ["owner"] = "Joint" });

        var response = await Get("/api/admin/statements");
        await Verify(response);
    }
}

public class GetStatementTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Returns_the_statement_with_its_staged_rows()
    {
        await Given.StatementWithRowsAsync();
        var response = await Get("/api/admin/statements/cstmt0000000000000000001");
        await Verify(response);
    }

    /// Every row of a real HSBC statement as the detail view returns it. The
    /// direction on each row is the column the figure was printed in, carried
    /// through to isCredit — a row flipped here is a row the parser read
    /// backwards, and nothing else about the response would look wrong.
    [Fact]
    public async Task Returns_the_staged_rows_of_a_bank_that_is_not_Amex()
    {
        await Given.NothingAsync();
        await PostFile("/api/admin/import/hsbc",
            Statement("2026-04-09-hsbc-statement.pdf"), "2026-04-09-hsbc-statement.pdf",
            fields: new Dictionary<string, string> { ["owner"] = "Joint" });

        var response = await Get("/api/admin/statements/cgen00000000000000000001");
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_statement()
    {
        await Given.NothingAsync();
        var response = await Get("/api/admin/statements/nope");
        await Verify(response);
    }
}

public class DownloadStatementTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Answers_gone_when_the_row_exists_but_the_file_does_not()
    {
        await Given.StatementWithRowsAsync();
        var response = await Get("/api/admin/statements/cstmt0000000000000000001/file");
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_statement()
    {
        await Given.NothingAsync();
        var response = await Get("/api/admin/statements/nope/file");
        await Verify(response);
    }
}

/// Re-parse re-runs the current parser over bytes already on the volume, which
/// is how a parser fix reaches a statement without the original file.
public class ReparseStatementTests(ClamApiFactory api) : ApiTest(api)
{
    private const string Amex = "2026-01-24-amex.pdf";
    private const string Hsbc = "2026-04-09-hsbc-statement.pdf";
    private const string Uploaded = "cgen00000000000000000001";

    private Task<HttpResponseMessage> Upload(string bank, string fileName, string owner)
        => PostFile($"/api/admin/import/{bank}", Statement(fileName), fileName,
            fields: new Dictionary<string, string> { ["owner"] = owner });

    /// Re-parsing an unchanged file replaces its rows with identical ones, so
    /// the count comes back out matching what went in and nothing is reported
    /// as a duplicate — this statement's own rows are cleared before the ids
    /// are compared, so a duplicate here would mean two statements overlap.
    [Fact]
    public async Task Re_parses_an_Amex_statement_and_reports_what_it_replaced()
    {
        await Given.NothingAsync();
        await Upload("amex", Amex, "Alex");

        var response = await Post($"/api/admin/statements/{Uploaded}/reparse");
        await Verify(response);
    }

    /// HSBC re-parses here, where the Express route this shadows refuses every
    /// bank but Amex — that refusal was the absence of a parser, and this
    /// service has one.
    [Fact]
    public async Task Re_parses_an_HSBC_statement_which_the_Express_route_refuses()
    {
        await Given.NothingAsync();
        await Upload("hsbc", Hsbc, "Joint");

        var response = await Post($"/api/admin/statements/{Uploaded}/reparse");
        await Verify(response);
    }

    /// The normalised transactions go too, not just the staged rows. Re-parsing
    /// a processed statement that left them behind would double every figure it
    /// contributed the moment it was processed again.
    [Fact]
    public async Task Replaces_the_transactions_a_processed_statement_produced()
    {
        await Given.SeededWithUncategorisedAsync();
        await Upload("hsbc", Hsbc, "Joint");
        await Post("/api/admin/process");

        var response = await Post($"/api/admin/statements/{Uploaded}/reparse");
        await Verify(response);
    }

    /// A bank with no statement parser at all. Every bank that uploads a PDF is
    /// re-parseable now, so the case left is a feed that never had one: Monzo
    /// arrives over its API, and a StatementFiles row naming it has no document
    /// behind it to re-read. It answers rather than throwing, because the
    /// statement row is legitimate — only the re-read is impossible.
    [Fact]
    public async Task Refuses_a_bank_it_has_no_parser_for()
    {
        await Given.StatementOfAnUnparseableBankAsync();

        var response = await Post("/api/admin/statements/cstmt0000000000000000002/reparse");
        await Verify(response);
    }

    /// The row exists, the bytes do not — 410, the same distinction the download
    /// slice draws. The staged rows must survive: there is nothing to replace
    /// them with.
    [Fact]
    public async Task Answers_gone_when_the_stored_pdf_is_missing()
    {
        await Given.StatementWithRowsAsync();

        var response = await Post("/api/admin/statements/cstmt0000000000000000001/reparse");
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_statement()
    {
        await Given.NothingAsync();

        var response = await Post("/api/admin/statements/nope/reparse");
        await Verify(response);
    }
}

public class DeleteStatementTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Deletes_the_statement_and_reports_what_went_with_it()
    {
        await Given.StatementWithRowsAsync();
        var response = await Delete("/api/admin/statements/cstmt0000000000000000001");
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_statement()
    {
        await Given.NothingAsync();
        var response = await Delete("/api/admin/statements/nope");
        await Verify(response);
    }
}
