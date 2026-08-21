using Clam.Api.Features.Import.ImportBarclays;

namespace Clam.Api.Tests.Features;

/// The Barclaycard parser, read against the real statements. No fixture and no
/// database — this half of the import pipeline is pure.
public class BarclaysStatementParserTests
{
    /// Barclays' own download names, kept verbatim. The statement month is a
    /// separate parameter only so the snapshot files are named something a person
    /// can find.
    private const string January = "Monthly BarclayCard Statement_26-JAN-26  270310481775250939803.pdf";
    private const string February = "Monthly BarclayCard Statement_24-FEB-26  250354261775250950657.pdf";
    private const string March = "Monthly BarclayCard Statement_24-MAR-26  250345451775250963464.pdf";

    private static byte[] Statement(string fileName)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Statements", fileName));

    /// Every row of every statement with the id it will be staged under.
    ///
    /// Large on purpose: the failure worth guarding against is one figure landing
    /// on the wrong row, or the two printed columns being read interleaved, and
    /// neither changes anything else about the output. Only row-by-row approval
    /// catches that.
    [Theory]
    [InlineData("2026-01", January)]
    [InlineData("2026-02", February)]
    [InlineData("2026-03", March)]
    public async Task Reads_every_row_of_a_real_statement(string month, string fileName)
    {
        var parsed = BarclaysStatementParser.Parse(Statement(fileName));

        Assert.True(parsed.Ok, parsed.Error);

        await Verify(new
        {
            parsed.StatementDate,
            RowCount = parsed.Rows.Count,
            Rows = BarclaysBusinessKeys.Assign(parsed.Rows).Select(k => new
            {
                k.TransactionId,
                k.Row.Date,
                k.Row.Description,
                k.Row.Amount,
                k.Row.IsCredit,
            }),
        }).UseParameters(month);
    }

    /// The statement's own arithmetic, asserted here rather than only inside the
    /// parser: previous balance − payments + new charges = new balance, and the
    /// parsed rows are what those two figures are made of.
    ///
    /// A parse that fails this is rejected outright, so `Ok` is the assertion —
    /// but a passing `Ok` says nothing about which identity held, and this spells
    /// out the one the statement is read for.
    [Theory]
    [InlineData(January, "2110.29", "901.37")]
    [InlineData(February, "901.37", "720.25")]
    [InlineData(March, "720.25", "707.31")]
    public void Balances_the_month_against_the_statements_own_figures(
        string fileName, string previousBalance, string newBalance)
    {
        var parsed = BarclaysStatementParser.Parse(Statement(fileName));
        Assert.True(parsed.Ok, parsed.Error);

        var payments = parsed.Rows.Where(r => r.IsCredit).Sum(Pence);
        var charges = parsed.Rows.Where(r => !r.IsCredit).Sum(Pence);

        Assert.Equal(Pence(newBalance), Pence(previousBalance) - payments + charges);
    }

    /// Consecutive statements meet at the boundary: February opens on the balance
    /// January closed on. Reading the three in order therefore has to chain, which
    /// no single statement can demonstrate.
    [Fact]
    public void Chains_across_three_consecutive_statements()
    {
        var closing = new[] { January, February, March }
            .Select(f => BarclaysStatementParser.Parse(Statement(f)))
            .Select(p => p.Rows.Where(r => !r.IsCredit).Sum(Pence) - p.Rows.Where(r => r.IsCredit).Sum(Pence))
            .ToList();

        // 2,110.29 opening, then each month's charges less its payments.
        var balance = 211029L;
        var balances = closing.Select(delta => balance += delta).ToList();

        Assert.Equal([90137L, 72025L, 70731L], balances);
    }

    /// The statement is set as two magazine columns, and the entries flow down
    /// the left half before continuing at the top of the right. Read as one table
    /// they interleave by y, which puts the 13th of the month between the 22nd and
    /// the 24th. Dates never going backwards is what proves the fold was honoured.
    [Fact]
    public void Reads_the_two_printed_columns_in_reading_order()
    {
        var rows = BarclaysStatementParser.Parse(Statement(January)).Rows;

        // The payment credit is dated at the end of the period and printed first,
        // in its own section, so ordering is asserted over the charges.
        var charges = rows.Where(r => !r.IsCredit).Select(r => r.Date).ToList();

        Assert.Equal(charges.Order(StringComparer.Ordinal), charges);

        // And the fold really was crossed: January's charges start in December.
        Assert.StartsWith("2025-12", charges[0], StringComparison.Ordinal);
        Assert.StartsWith("2026-01", charges[^1], StringComparison.Ordinal);
    }

    /// Direction comes from the section an entry was printed under, never from
    /// its description. The Express importer tested for "Payment By Direct Debit",
    /// which is what the credit is called on these statements rather than
    /// anything the bank owes anyone.
    [Fact]
    public void Reads_direction_from_the_section_not_the_description()
    {
        var rows = BarclaysStatementParser.Parse(Statement(January)).Rows;

        var credit = Assert.Single(rows, r => r.IsCredit);
        Assert.Equal("2,110.29", credit.Amount);
        Assert.DoesNotContain(rows.Where(r => !r.IsCredit), r =>
            r.Description.Contains("Payment By Direct Debit", StringComparison.OrdinalIgnoreCase));
    }

    /// A foreign charge prints the currency, rate and fee on lines beneath itself,
    /// indented under the description. They are description, not entries of their
    /// own, and not noise to drop either — the sterling figure is the charge.
    [Fact]
    public void Keeps_the_currency_lines_a_foreign_charge_prints_beneath_itself()
    {
        var rows = BarclaysStatementParser.Parse(Statement(February)).Rows;

        var hotel = Assert.Single(rows, r => r.Description.StartsWith("Hotel Le Pelvoux", StringComparison.Ordinal));

        Assert.Equal("585.48", hotel.Amount);
        Assert.Contains("671.22 Euro", hotel.Description, StringComparison.Ordinal);
        Assert.Contains("Incl Non Sterling Trans Fee of £0.00", hotel.Description, StringComparison.Ordinal);
    }

    /// "Ways to pay" is printed directly beneath the last transaction of a column.
    /// It sits at the outer margin where a heading does, not at the description
    /// indent, which is the whole reason the parser can tell the two apart.
    [Fact]
    public void Does_not_swallow_the_marketing_printed_under_the_last_transaction()
    {
        var rows = BarclaysStatementParser.Parse(Statement(January)).Rows;

        // "Direct Debit" is deliberately absent from this list: it is also the
        // name of the one legitimate credit on the statement.
        foreach (var marketing in new[] { "Ways to pay", "Faster Payment", "Sort code", "QR code", "Barclaycard app" })
            Assert.DoesNotContain(rows, r => r.Description.Contains(marketing, StringComparison.OrdinalIgnoreCase));
    }

    /// Parsing the same bytes twice has to produce the same ids, or a re-uploaded
    /// statement double-counts instead of being recognised.
    [Fact]
    public void Assigns_the_same_ids_every_time_it_parses_the_same_statement()
    {
        var pdf = Statement(January);

        var first = BarclaysBusinessKeys.Assign(BarclaysStatementParser.Parse(pdf).Rows);
        var second = BarclaysBusinessKeys.Assign(BarclaysStatementParser.Parse(pdf).Rows);

        Assert.Equal(first.Select(k => k.TransactionId), second.Select(k => k.TransactionId));
    }

    /// March prints the same £7.50 golf booking twice, on the same day, either
    /// side of the fold. They are two charges, so they must key distinctly.
    [Fact]
    public void Keeps_two_identical_charges_on_one_statement_apart()
    {
        var keyed = BarclaysBusinessKeys.Assign(BarclaysStatementParser.Parse(Statement(March)).Rows);

        Assert.Distinct(keyed.Select(k => k.TransactionId));
    }

    [Fact]
    public void Rejects_another_banks_statement()
    {
        var parsed = BarclaysStatementParser.Parse(Statement("2026-01-24-amex.pdf"));

        Assert.False(parsed.Ok);
        Assert.Equal(422, parsed.Status);
        Assert.Contains("American Express", parsed.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_bytes_that_are_not_a_pdf()
    {
        var parsed = BarclaysStatementParser.Parse("this is not a PDF"u8.ToArray());

        Assert.False(parsed.Ok);
        Assert.Equal(400, parsed.Status);
    }

    private static long Pence(BarclaysRow row) => Pence(row.Amount);

    private static long Pence(string printed) =>
        (long)decimal.Round(
            decimal.Parse(printed.Replace(",", "", StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture) * 100,
            0,
            MidpointRounding.AwayFromZero);
}
