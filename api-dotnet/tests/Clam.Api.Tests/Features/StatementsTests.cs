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
