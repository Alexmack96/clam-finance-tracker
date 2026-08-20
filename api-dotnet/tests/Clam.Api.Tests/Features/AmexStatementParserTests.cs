using System.Globalization;
using Clam.Api.Features.Import.ImportAmex;

namespace Clam.Api.Tests.Features;

/// The parser, read against the real statements rather than a fixture someone
/// wrote to match it.
///
/// No fixture and no database: this is the one part of the import pipeline that
/// is pure, and keeping it that way is what makes "run it over four actual PDFs"
/// a cheap test rather than an integration suite.
public class AmexStatementParserTests
{
    private static byte[] Statement(string fileName)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Statements", fileName));

    /// Every row of every statement, with the id it will be staged under.
    ///
    /// A large snapshot on purpose. The failure this guards against is not "the
    /// parser threw" — it is one amount landing on the wrong row, which changes
    /// nothing else about the output. Only row-by-row approval catches that, and
    /// the same property makes the file a precise diff when a parser change is
    /// deliberate.
    [Theory]
    [InlineData("2026-01-24-amex.pdf")]
    [InlineData("2026-02-24-amex.pdf")]
    [InlineData("2026-03-24-amex.pdf")]
    [InlineData("2026-07-24-amex.pdf")]
    public async Task Reads_every_row_of_a_real_statement(string fileName)
    {
        var parsed = AmexStatementParser.Parse(Statement(fileName));

        Assert.True(parsed.Ok, parsed.Error);

        var keyed = AmexBusinessKeys.Assign(parsed.Rows, "Alex");

        await Verify(new
        {
            parsed.StatementDate,
            RowCount = parsed.Rows.Count,
            Debits = Total(parsed.Rows.Where(r => !r.IsCredit)),
            Credits = Total(parsed.Rows.Where(r => r.IsCredit)),
            Rows = keyed.Select(k => new
            {
                k.TransactionId,
                k.Row.TransactionDate,
                k.Row.ProcessDate,
                k.Row.Description,
                k.Row.Amount,
                k.Row.IsCredit,
                k.Row.ForeignAmount,
                k.Row.ForeignCurrency,
            }),
        }).UseParameters(fileName);
    }

    /// Two genuinely distinct charges can be identical on every field the
    /// statement prints — 10 July has two LIME*RIDE KHJA £1.70 hires, and both
    /// are real. They must survive as two rows with two ids, or the second is
    /// silently swallowed as a duplicate of the first.
    [Fact]
    public void Keeps_identical_charges_on_the_same_day_as_separate_ids()
    {
        var parsed = AmexStatementParser.Parse(Statement("2026-07-24-amex.pdf"));
        var keyed = AmexBusinessKeys.Assign(parsed.Rows, "Alex");

        var lime = keyed
            .Where(k => k.Row.Description.StartsWith("LIME*RIDE", StringComparison.Ordinal)
                     && k.Row.TransactionDate == "2026-07-10"
                     && k.Row.Amount == "1.70")
            .ToList();

        Assert.Equal(2, lime.Count);
        Assert.Equal(2, lime.Select(k => k.TransactionId).Distinct(StringComparer.Ordinal).Count());

        // The suffix is a suffix, not a different hash: both rows still say they
        // are the same charge, which is what makes the pair legible in the data.
        Assert.Equal(lime[0].TransactionId, lime[1].TransactionId.Split('-')[0]);
    }

    /// The whole point of hashing content rather than counting: parsing the same
    /// bytes twice has to produce the same ids, or a re-uploaded statement
    /// double-counts instead of being recognised.
    [Fact]
    public void Assigns_the_same_ids_every_time_it_parses_the_same_statement()
    {
        var pdf = Statement("2026-01-24-amex.pdf");

        var first = AmexBusinessKeys.Assign(AmexStatementParser.Parse(pdf).Rows, "Alex");
        var second = AmexBusinessKeys.Assign(AmexStatementParser.Parse(pdf).Rows, "Alex");

        Assert.Equal(first.Select(k => k.TransactionId), second.Select(k => k.TransactionId));
    }

    /// Amex is the one card shared between people, so the owner is part of the
    /// key. Without it, Casey's statement would lose any row that matched one of
    /// Alex's on every printed field.
    [Fact]
    public void Gives_the_same_row_different_ids_for_different_owners()
    {
        var rows = AmexStatementParser.Parse(Statement("2026-01-24-amex.pdf")).Rows;

        var alex = AmexBusinessKeys.Assign(rows, "Alex").Select(k => k.TransactionId);
        var casey = AmexBusinessKeys.Assign(rows, "Casey").Select(k => k.TransactionId);

        Assert.Empty(alex.Intersect(casey, StringComparer.Ordinal));
    }

    /// A PDF that is not a statement at all fails in the extractor, and must
    /// come back as a 400 rather than as an exception out of the endpoint.
    [Fact]
    public void Rejects_bytes_that_are_not_a_pdf()
    {
        var parsed = AmexStatementParser.Parse("this is not a PDF"u8.ToArray());

        Assert.False(parsed.Ok);
        Assert.Equal(400, parsed.Status);
    }

    private static string Total(IEnumerable<AmexRow> rows) =>
        rows.Sum(r => decimal.Parse(r.Amount.Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture))
            .ToString("0.00", CultureInfo.InvariantCulture);
}
