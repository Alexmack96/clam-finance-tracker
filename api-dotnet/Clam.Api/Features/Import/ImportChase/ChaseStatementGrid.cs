namespace Clam.Api.Features.Import.ImportChase;

/// Where Chase's three columns fall. The geometry itself is in
/// <see cref="StatementGrid"/>, shared with the other banks.
internal static class ChaseStatementGrid
{
    /// Observed anchors: transaction dates start at 26.6, descriptions at 111.6,
    /// and amounts are right-aligned to 485.7.
    ///
    /// A foreign charge prints two extra lines under it — the posting date and
    /// currency, then "2.80 X 1.346428571 (EXCHG RATE)" — indented to 115.4 and
    /// 126.2. Both land in the description column, which is what keeps their
    /// figures from ever being read as an amount.
    private static readonly double[] ColumnBounds = [100, 440];

    internal const int ColDate = 0;
    internal const int ColDescription = 1;
    internal const int ColAmount = 2;

    internal static List<string[]> Build(byte[] pdf) => StatementGrid.Build(pdf, ColumnBounds);
}
