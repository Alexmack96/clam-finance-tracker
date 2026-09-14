using Clam.Api.Infrastructure.Statements;

namespace Clam.Api.Tests.Features;

/// How an uploaded statement is named on the volume. The labels are the ones
/// each parser really produces, taken from its snapshot tests.
///
/// No fixture and no database: a throwaway directory is all the store touches.
public sealed class StatementStoreKeyTests : IDisposable
{
    private const string Hash = "b344dfeb8ac9f00dcafe";

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "clam-statements-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData("amex", "24/02/26", "amex/Alex/2026-02-24-amex.pdf")]
    [InlineData("barclays", "February 2026", "barclays/Alex/2026-02-barclays.pdf")]
    [InlineData("chase", "Feb 2026", "chase/Alex/2026-02-chase.pdf")]
    [InlineData("sofi", "Sept 2026", "sofi/Alex/2026-09-sofi.pdf")]
    // A range is named by its end, the day the statement closed.
    [InlineData("hsbc", "10 March to 9 April 2026", "hsbc/Alex/2026-04-09-hsbc.pdf")]
    [InlineData("santander", "22 Dec 2025 to 20 Jan 2026", "santander/Alex/2026-01-20-santander.pdf")]
    public void Names_a_statement_by_its_iso_date_and_bank(string bank, string label, string expected)
    {
        Assert.Equal(expected, new FileSystemStatementStore(_dir).KeyFor(bank, "Alex", label, Hash));
    }

    [Fact]
    public void Keeps_the_text_of_a_label_that_does_not_parse()
    {
        Assert.Equal(
            "amex/Alex/Q1-statement-amex.pdf",
            new FileSystemStatementStore(_dir).KeyFor("amex", "Alex", "Q1 statement", Hash));
    }

    [Fact]
    public void Names_a_statement_with_no_date_undated()
    {
        Assert.Equal(
            "amex/Alex/undated-amex.pdf",
            new FileSystemStatementStore(_dir).KeyFor("amex", "Alex", null, Hash));
    }

    /// A reissued statement with the same date is different bytes under the same
    /// name. Overwriting would leave the first statement's row pointing at the
    /// second statement's PDF.
    [Fact]
    public async Task A_taken_name_gets_a_short_hash_rather_than_overwriting()
    {
        var store = new FileSystemStatementStore(_dir);
        await store.SaveAsync("amex/Alex/2026-02-24-amex.pdf", [1]);

        Assert.Equal(
            "amex/Alex/2026-02-24-amex-b344dfeb.pdf",
            store.KeyFor("amex", "Alex", "24/02/26", Hash));
    }
}
