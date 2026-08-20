using Clam.Api.Features.Import.ImportHsbc;

namespace Clam.Api.Tests.Features;

/// The HSBC parser, read against the real statements. No fixture and no
/// database — this half of the import pipeline is pure.
public class HsbcStatementParserTests
{
    private static byte[] Statement(string fileName)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Statements", fileName));

    /// Every row of every statement with the id it will be staged under.
    ///
    /// Large on purpose: the failure worth guarding against is one figure
    /// landing in the wrong column or on the wrong row, which changes nothing
    /// else about the output. Only row-by-row approval catches that.
    [Theory]
    [InlineData("2026-04-09-hsbc-statement.pdf")]
    [InlineData("2026-05-09-hsbc-statement.pdf")]
    public async Task Reads_every_row_of_a_real_statement(string fileName)
    {
        var parsed = HsbcStatementParser.Parse(Statement(fileName));

        Assert.True(parsed.Ok, parsed.Error);

        await Verify(new
        {
            parsed.StatementDate,
            RowCount = parsed.Rows.Count,
            Rows = HsbcBusinessKeys.Assign(parsed.Rows).Select(k => new
            {
                k.TransactionId,
                k.Row.Date,
                k.Row.PaymentType,
                k.Row.Description,
                k.Row.MoneyOut,
                k.Row.MoneyIn,
                k.Row.Balance,
            }),
        }).UseParameters(fileName);
    }

    /// Direction comes from the column a figure was printed in, never from the
    /// payment type. This is the specific trap: some incoming payments are typed
    /// BP, the same code most outgoing ones use, so a type-driven parser books
    /// them backwards.
    [Fact]
    public void Reads_direction_from_the_column_not_the_payment_type()
    {
        var rows = HsbcStatementParser.Parse(Statement("2026-04-09-hsbc-statement.pdf")).Rows;

        // Every row is one direction or the other, never both and never neither.
        Assert.All(rows, r => Assert.True(
            (r.MoneyOut is null) != (r.MoneyIn is null),
            $"{r.Date} {r.PaymentType} {r.Description} has MoneyOut={r.MoneyOut}, MoneyIn={r.MoneyIn}"));

        // And BP carries real money, which is what defeats type-driven parsing:
        // the same code appears on payments in both directions.
        Assert.Contains(rows, r => r.PaymentType == "BP" && r.MoneyOut is not null);
    }

    /// Parsing the same bytes twice has to produce the same ids, or a re-uploaded
    /// statement double-counts instead of being recognised.
    [Fact]
    public void Assigns_the_same_ids_every_time_it_parses_the_same_statement()
    {
        var pdf = Statement("2026-04-09-hsbc-statement.pdf");

        var first = HsbcBusinessKeys.Assign(HsbcStatementParser.Parse(pdf).Rows);
        var second = HsbcBusinessKeys.Assign(HsbcStatementParser.Parse(pdf).Rows);

        Assert.Equal(first.Select(k => k.TransactionId), second.Select(k => k.TransactionId));
    }

    /// The interest-rates footer prints "Cre dit inte re s t", which a payment-type
    /// pattern that lets a code run into the next word reads as a CR transaction.
    /// The TypeScript original did exactly that.
    [Fact]
    public void Does_not_invent_transactions_out_of_the_page_footer()
    {
        var rows = HsbcStatementParser.Parse(Statement("2026-04-09-hsbc-statement.pdf")).Rows;

        Assert.DoesNotContain(rows, r =>
            r.Description.Contains("interest", StringComparison.OrdinalIgnoreCase)
            || r.Description.Contains("Overdraft", StringComparison.OrdinalIgnoreCase)
            || r.Description.Contains("Centenary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_another_banks_statement()
    {
        var parsed = HsbcStatementParser.Parse(Statement("2026-01-24-amex.pdf"));

        Assert.False(parsed.Ok);
        Assert.Equal(422, parsed.Status);
        Assert.Contains("American Express", parsed.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_bytes_that_are_not_a_pdf()
    {
        var parsed = HsbcStatementParser.Parse("this is not a PDF"u8.ToArray());

        Assert.False(parsed.Ok);
        Assert.Equal(400, parsed.Status);
    }
}
