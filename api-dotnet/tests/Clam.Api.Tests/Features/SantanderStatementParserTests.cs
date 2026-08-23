using Clam.Api.Features.Import.ImportSantander;

namespace Clam.Api.Tests.Features;

/// The Santander parser, read against the real statements. No fixture and no
/// database — this half of the import pipeline is pure.
public class SantanderStatementParserTests
{
    private const string December = "2025-12-22-to-2026-20-01-santander.pdf";
    private const string January = "2026-21-01-to-2026-02-20-santander.pdf";
    private const string February = "2026-21-02-to-2026-20-03-santander.pdf";
    private const string March = "2026-21-03-to-2026-20-04-santander.pdf";

    private static byte[] Statement(string fileName)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Statements", fileName));

    /// Every row of every statement with the id it will be staged under.
    ///
    /// Large on purpose: the failure worth guarding against is a figure landing
    /// in the wrong money column, which flips a payment's direction and changes
    /// nothing else about the output. Only row-by-row approval catches that.
    [Theory]
    [InlineData("2025-12", December)]
    [InlineData("2026-01", January)]
    [InlineData("2026-02", February)]
    [InlineData("2026-03", March)]
    public async Task Reads_every_row_of_a_real_statement(string month, string fileName)
    {
        var parsed = SantanderStatementParser.Parse(Statement(fileName));

        Assert.True(parsed.Ok, parsed.Error);

        await Verify(new
        {
            parsed.StatementDate,
            RowCount = parsed.Rows.Count,
            Rows = SantanderBusinessKeys.Assign(parsed.Rows).Select(k => new
            {
                k.TransactionId,
                k.Row.Date,
                k.Row.Description,
                k.Row.MoneyIn,
                k.Row.MoneyOut,
                k.Row.Balance,
            }),
        }).UseParameters(month);
    }

    /// The statement's own arithmetic, asserted here rather than only inside the
    /// parser: opening + money in − money out = closing, and the parsed rows are
    /// what those two totals are made of.
    ///
    /// A parse that fails this is rejected outright, so `Ok` is the assertion —
    /// but a passing `Ok` says nothing about which identity held, and this
    /// spells out the one the statement is read for.
    [Theory]
    [InlineData(December, "1,365.28")]
    [InlineData(January, "436.25")]
    [InlineData(February, "2,415.06")]
    [InlineData(March, "5,203.75")]
    public void Rows_account_for_the_closing_balance(string fileName, string closing)
    {
        var parsed = SantanderStatementParser.Parse(Statement(fileName));

        Assert.True(parsed.Ok, parsed.Error);
        Assert.Equal(closing, parsed.Rows[^1].Balance);
    }

    /// The December statement runs 22nd December 2025 to 20th January 2026 and
    /// prints both months' entries with no year on either. A parser that assumed
    /// the statement's year dates the December half twelve months late.
    [Fact]
    public void Dates_a_month_later_than_the_period_ends_to_the_previous_year()
    {
        var parsed = SantanderStatementParser.Parse(Statement(December));

        Assert.True(parsed.Ok, parsed.Error);
        Assert.Contains(parsed.Rows, r => r.Date.StartsWith("2025-12", StringComparison.Ordinal));
        Assert.Contains(parsed.Rows, r => r.Date.StartsWith("2026-01", StringComparison.Ordinal));
    }

    /// Direction is the column the figure was printed in. Both are never set on
    /// one row, and every row has one or the other.
    [Theory]
    [InlineData(December)]
    [InlineData(January)]
    [InlineData(February)]
    [InlineData(March)]
    public void Reads_direction_from_the_column_not_the_wording(string fileName)
    {
        var parsed = SantanderStatementParser.Parse(Statement(fileName));

        Assert.True(parsed.Ok, parsed.Error);
        Assert.All(parsed.Rows, r => Assert.True(
            (r.MoneyIn is null) != (r.MoneyOut is null),
            $"{r.Date} \"{r.Description}\" has in={r.MoneyIn ?? "-"} out={r.MoneyOut ?? "-"}"));
    }

    /// A statement whose description wrapped onto the lines either side of its
    /// own date. The standing order to Foxtons is printed over three lines with
    /// the date and amount centred against the middle one, so a parser that
    /// grouped by baseline alone reads it as three separate things.
    [Fact]
    public void Keeps_a_wrapped_description_with_its_own_row()
    {
        var parsed = SantanderStatementParser.Parse(Statement(January));

        Assert.True(parsed.Ok, parsed.Error);

        var standingOrder = Assert.Single(parsed.Rows, r => r.Description.Contains("Foxtons", StringComparison.Ordinal));
        Assert.Equal("2026-02-19", standingOrder.Date);
        Assert.Equal("2,400.00", standingOrder.MoneyOut);
        Assert.EndsWith("MANDATE NO 0101", standingOrder.Description, StringComparison.Ordinal);
    }

    /// Another bank's statement. It reads fine and is simply the wrong document,
    /// which is a 422 naming the bank it actually found.
    [Fact]
    public void Refuses_another_banks_statement()
    {
        var parsed = SantanderStatementParser.Parse(Statement("2026-01-24-amex.pdf"));

        Assert.False(parsed.Ok);
        Assert.Equal(422, parsed.Status);
        Assert.Contains("American Express", parsed.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_something_that_is_not_a_pdf()
    {
        var parsed = SantanderStatementParser.Parse("not a pdf"u8.ToArray());

        Assert.False(parsed.Ok);
        Assert.Equal(400, parsed.Status);
    }
}
