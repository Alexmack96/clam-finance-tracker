namespace Clam.Api.Features.Import.ImportSofi;

/// Where SoFi's five columns fall. The geometry itself is in
/// <see cref="StatementGrid"/>, shared with the other banks.
internal static class SofiStatementGrid
{
    /// Observed anchors: dates start at 20.9, types at 105.9, descriptions at
    /// 218.9, and the two money columns are right-aligned to 501.3 (Amount) and
    /// 591.4 (Balance).
    ///
    /// The "Transaction ID:" line under each entry is indented to the
    /// description column, which is where it is read from.
    private static readonly double[] ColumnBounds = [100, 215, 440, 530];

    internal const int ColDate = 0;
    internal const int ColType = 1;
    internal const int ColDescription = 2;
    internal const int ColAmount = 3;
    internal const int ColBalance = 4;

    internal static List<string[]> Build(byte[] pdf) => StatementGrid.Build(pdf, ColumnBounds);
}
