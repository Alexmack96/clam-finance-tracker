using Clam.Api.Features.Import.ImportChase;

namespace Clam.Api.Tests.Features;

/// The Chase parser, read against the real statements. No fixture and no
/// database — this half of the import pipeline is pure.
public class ChaseStatementParserTests
{
    /// Not a fixture for reading rows: see
    /// <see cref="Refuses_a_statement_whose_transactions_are_on_an_image_page"/>.
    private const string January = "2026-01-22-chase.pdf";

    private const string February = "2026-02-22-chase.pdf";
    private const string March = "2026-03-22-chase.pdf";

    private static byte[] Statement(string fileName)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Statements", fileName));

    /// Every row of every statement with the id it will be staged under.
    ///
    /// Large on purpose: the failure worth guarding against is a foreign
    /// charge's exchange-rate line being read as a transaction of its own, which
    /// adds a plausible-looking row and changes nothing else about the output.
    /// Only row-by-row approval catches that.
    [Theory]
    [InlineData("2026-02", February)]
    [InlineData("2026-03", March)]
    public async Task Reads_every_row_of_a_real_statement(string month, string fileName)
    {
        var parsed = ChaseStatementParser.Parse(Statement(fileName));

        Assert.True(parsed.Ok, parsed.Error);

        await Verify(new
        {
            parsed.StatementDate,
            RowCount = parsed.Rows.Count,
            Rows = ChaseBusinessKeys.Assign(parsed.Rows).Select(k => new
            {
                k.TransactionId,
                k.Row.Date,
                k.Row.Description,
                k.Row.Amount,
                k.Row.IsCredit,
            }),
        }).UseParameters(month);
    }

    [Theory]
    [InlineData(February, "Feb 2026")]
    [InlineData(March, "Mar 2026")]
    public void Labels_the_statement_by_its_closing_month(string fileName, string label)
    {
        var parsed = ChaseStatementParser.Parse(Statement(fileName));

        Assert.True(parsed.Ok, parsed.Error);
        Assert.Equal(label, parsed.StatementDate);
    }

    /// A credit is the sign on the figure, and the amount is staged unsigned:
    /// the process step converts it to sterling, and a negative would come back
    /// as a negative expense rather than as income.
    [Theory]
    [InlineData(February)]
    [InlineData(March)]
    public void Stages_amounts_unsigned_with_the_direction_on_the_row(string fileName)
    {
        var parsed = ChaseStatementParser.Parse(Statement(fileName));

        Assert.True(parsed.Ok, parsed.Error);
        Assert.All(parsed.Rows, r => Assert.DoesNotContain('-', r.Amount));
        Assert.Contains(parsed.Rows, r => r.IsCredit);
    }

    /// A foreign charge prints its currency and exchange rate on two lines below
    /// itself. Both carry figures, and reading either as a transaction would add
    /// a row that looks entirely plausible.
    [Fact]
    public void Ignores_the_exchange_rate_lines_under_a_foreign_charge()
    {
        var parsed = ChaseStatementParser.Parse(Statement(February));

        Assert.True(parsed.Ok, parsed.Error);
        Assert.DoesNotContain(parsed.Rows, r => r.Description.Contains("EXCHG RATE", StringComparison.Ordinal));
        Assert.DoesNotContain(parsed.Rows, r => r.Description.Contains("POUND STERLING", StringComparison.Ordinal));

        // The charge itself is kept, in the dollars it was billed in.
        var hotel = Assert.Single(parsed.Rows, r => r.Description.Contains("DUESSELDORF", StringComparison.Ordinal));
        Assert.Equal("518.46", hotel.Amount);
        Assert.False(hotel.IsCredit);
    }

    /// The January statement has a page of transactions flattened to an image:
    /// 0 letters, 1 picture, $2,587.83 of purchases that are not in the file as
    /// text at all. Nothing can read them, and the only two honest outcomes are
    /// importing two thirds of a statement or importing none of it.
    ///
    /// This takes the second, which is the whole reason the reconciliation
    /// exists — the Express importer had none, so it imported the two thirds and
    /// said nothing. The message has to name the page, because "the totals don't
    /// match" sends you looking for a parser bug that isn't there.
    [Fact]
    public void Refuses_a_statement_whose_transactions_are_on_an_image_page()
    {
        var parsed = ChaseStatementParser.Parse(Statement(January));

        Assert.False(parsed.Ok);
        Assert.Equal(422, parsed.Status);
        Assert.Contains("Page 4 of this PDF is an image", parsed.Error!, StringComparison.Ordinal);
    }

    /// Another bank's statement. It reads fine and is simply the wrong document,
    /// which is a 422 naming the bank it actually found.
    [Fact]
    public void Refuses_another_banks_statement()
    {
        var parsed = ChaseStatementParser.Parse(Statement("2026-01-24-amex.pdf"));

        Assert.False(parsed.Ok);
        Assert.Equal(422, parsed.Status);
        Assert.Contains("American Express", parsed.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_something_that_is_not_a_pdf()
    {
        var parsed = ChaseStatementParser.Parse("not a pdf"u8.ToArray());

        Assert.False(parsed.Ok);
        Assert.Equal(400, parsed.Status);
    }
}
