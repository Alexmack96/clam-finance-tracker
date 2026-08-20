namespace Clam.Api.Features.Import.ImportAmex;

/// Where Amex's five columns fall, and nothing else — the geometry itself lives
/// in <see cref="StatementGrid"/>, shared with the other banks.
internal static class AmexStatementGrid
{
    /// Upper x-bounds for columns 0..3; anything further right is Amount £.
    /// Header anchors on these statements: Transaction Date 14.4, Process Date
    /// 57.6, Transaction Details 100.8, Foreign Spend 377.0, Amount £ 504.2
    /// (right-aligned, so its values start anywhere from ~489 to ~514).
    private static readonly double[] ColumnBounds = [50, 95, 340, 470];

    internal const int ColTransactionDate = 0;
    internal const int ColProcessDate = 1;
    internal const int ColDescription = 2;
    internal const int ColForeign = 3;
    internal const int ColAmount = 4;

    internal static List<string[]> Build(byte[] pdf) => StatementGrid.Build(pdf, ColumnBounds);
}
