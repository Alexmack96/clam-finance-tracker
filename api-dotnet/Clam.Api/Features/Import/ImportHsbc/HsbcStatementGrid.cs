namespace Clam.Api.Features.Import.ImportHsbc;

/// Where HSBC's four columns fall. The geometry itself is in
/// <see cref="StatementGrid"/>, shared with the other banks.
internal static class HsbcStatementGrid
{
    /// Upper x-bounds for the three money columns. Observed anchors on these
    /// statements: Paid out ~360-376, Paid in ~443-454, Balance ~518-523. The
    /// gaps are wide, so mid-point thresholds are robust; anything left of 340
    /// is still the details column.
    private static readonly double[] ColumnBounds = [340, 415, 495];

    internal const int ColDetails = 0;
    internal const int ColPaidOut = 1;
    internal const int ColPaidIn = 2;
    internal const int ColBalance = 3;

    internal static List<string[]> Build(byte[] pdf) => StatementGrid.Build(pdf, ColumnBounds);
}
