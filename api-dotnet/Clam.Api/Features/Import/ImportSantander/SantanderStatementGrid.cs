namespace Clam.Api.Features.Import.ImportSantander;

/// Where Santander's five columns fall. The geometry itself is in
/// <see cref="StatementGrid"/>, shared with the other banks.
internal static class SantanderStatementGrid
{
    /// Upper x-bounds for the four columns after the date. Observed anchors:
    /// dates start at 54.1, descriptions at 116.0, and the three money columns
    /// are right-aligned to 455.7 (Money in), 502.4 (Money out) and 549.0
    /// (Balance).
    ///
    /// The bounds classify a run by its *left* edge, which is why the
    /// description bound can sit as low as 424 without cutting a description in
    /// half: Santander sets its descriptions tightly enough that the whole of
    /// one coalesces into a single run starting at 116.0, however far right it
    /// runs. What the bound has to survive instead is a wide figure: a
    /// right-aligned "10,000.00" in the Money in column starts near 425, so the
    /// bound is set just below that and a five-figure single credit — which this
    /// account has never printed — would be read as description. The
    /// reconciliation is the backstop: a figure read into the wrong column
    /// fails the statement's own totals rather than being staged quietly.
    private static readonly double[] ColumnBounds = [100, 424, 465, 512];

    internal const int ColDate = 0;
    internal const int ColDescription = 1;
    internal const int ColMoneyIn = 2;
    internal const int ColMoneyOut = 3;
    internal const int ColBalance = 4;

    internal static List<StatementGrid.GridRow> Build(byte[] pdf) =>
        StatementGrid.BuildRows(pdf, ColumnBounds);
}
