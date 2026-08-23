using Clam.Api.Features.Import.ImportSofi;

namespace Clam.Api.Tests.Features;

/// The SoFi parser, read against the real statements. No fixture and no
/// database — this half of the import pipeline is pure.
public class SofiStatementParserTests
{
    private const string January = "SoFiMoneyStatement_2026-01-31.pdf";
    private const string February = "SoFiMoneyStatement_2026-02-28.pdf";
    private const string March = "SoFiMoneyStatement_2026-03-31.pdf";

    private static byte[] Statement(string fileName)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Statements", fileName));

    /// Every row of every statement with the id it will be staged under.
    ///
    /// Large on purpose: one PDF holds two accounts, and the failure worth
    /// guarding against is a Savings row being tagged as Checking — which
    /// changes no figure and no count, and turns an internal transfer into
    /// spending once the process step stops recognising it.
    [Theory]
    [InlineData("2026-01", January)]
    [InlineData("2026-02", February)]
    [InlineData("2026-03", March)]
    public async Task Reads_every_row_of_a_real_statement(string month, string fileName)
    {
        var parsed = SofiStatementParser.Parse(Statement(fileName));

        Assert.True(parsed.Ok, parsed.Error);

        await Verify(new
        {
            parsed.StatementDate,
            RowCount = parsed.Rows.Count,
            Rows = SofiBusinessKeys.Assign(parsed.Rows).Select(k => new
            {
                k.TransactionId,
                k.Row.Date,
                k.Row.Type,
                k.Row.Description,
                k.Row.Amount,
                k.Row.IsCredit,
                k.Row.Balance,
                k.Row.AccountType,
            }),
        }).UseParameters(month);
    }

    /// Both accounts are read, not just the first. The Savings table starts over
    /// a page later with its own header and its own balances, and a parser that
    /// stopped at the end of Checking would look entirely healthy.
    [Theory]
    [InlineData(January)]
    [InlineData(February)]
    [InlineData(March)]
    public void Reads_both_accounts_from_one_pdf(string fileName)
    {
        var parsed = SofiStatementParser.Parse(Statement(fileName));

        Assert.True(parsed.Ok, parsed.Error);
        Assert.Contains(parsed.Rows, r => r.AccountType == "Checking");
        Assert.Contains(parsed.Rows, r => r.AccountType == "Savings");
    }

    /// Every row carries SoFi's own id, and no two rows share one. These are the
    /// staged primary keys, so a blank or repeated one is not a cosmetic problem.
    [Theory]
    [InlineData(January)]
    [InlineData(February)]
    [InlineData(March)]
    public void Gives_every_row_the_id_sofi_printed_under_it(string fileName)
    {
        var parsed = SofiStatementParser.Parse(Statement(fileName));

        Assert.True(parsed.Ok, parsed.Error);
        Assert.All(parsed.Rows, r => Assert.NotEqual("", r.TransactionId));
        Assert.Equal(parsed.Rows.Count, parsed.Rows.Select(r => r.TransactionId).Distinct(StringComparer.Ordinal).Count());
    }

    /// A credit is the sign on the figure, and the amount is staged unsigned:
    /// the process step converts it to sterling, and a negative would come back
    /// as a negative expense rather than as income.
    [Theory]
    [InlineData(January)]
    [InlineData(February)]
    [InlineData(March)]
    public void Stages_amounts_unsigned_with_the_direction_on_the_row(string fileName)
    {
        var parsed = SofiStatementParser.Parse(Statement(fileName));

        Assert.True(parsed.Ok, parsed.Error);
        Assert.All(parsed.Rows, r => Assert.DoesNotContain('-', r.Amount));
        Assert.Contains(parsed.Rows, r => r.IsCredit);
        Assert.Contains(parsed.Rows, r => !r.IsCredit);
    }

    /// The two sides of a transfer between the account holder's own accounts.
    /// Both are kept as staged rows — the process step is what drops them, and
    /// it recognises them by description, so the descriptions have to survive
    /// the parse intact.
    [Fact]
    public void Keeps_both_sides_of_an_internal_transfer()
    {
        var parsed = SofiStatementParser.Parse(Statement(February));

        Assert.True(parsed.Ok, parsed.Error);

        var into = Assert.Single(parsed.Rows, r => r.Description == "From Savings - 3143");
        var outOf = Assert.Single(parsed.Rows, r => r.Description == "To Checking - 1598");

        Assert.Equal("Checking", into.AccountType);
        Assert.Equal("Savings", outOf.AccountType);
        Assert.Equal(into.Amount, outOf.Amount);
        Assert.True(into.IsCredit);
        Assert.False(outOf.IsCredit);
    }

    /// Another bank's statement. It reads fine and is simply the wrong document,
    /// which is a 422 naming the bank it actually found.
    [Fact]
    public void Refuses_another_banks_statement()
    {
        var parsed = SofiStatementParser.Parse(Statement("2026-01-24-amex.pdf"));

        Assert.False(parsed.Ok);
        Assert.Equal(422, parsed.Status);
        Assert.Contains("American Express", parsed.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_something_that_is_not_a_pdf()
    {
        var parsed = SofiStatementParser.Parse("not a pdf"u8.ToArray());

        Assert.False(parsed.Ok);
        Assert.Equal(400, parsed.Status);
    }
}
