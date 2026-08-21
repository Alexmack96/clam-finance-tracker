namespace Clam.Api.Features.Import.ImportBarclays;

/// Where Barclaycard's columns fall. The geometry itself is in
/// <see cref="StatementGrid"/>, shared with the other banks.
///
/// Two bands, not one table: the statement is set as two magazine columns and
/// the entries flow down the left half before continuing at the top of the
/// right. Each half is the same three columns, at its own offsets.
internal static class BarclaysStatementGrid
{
    /// The fold. Left-hand amounts end by ~297 and right-hand content starts at
    /// ~325, so the gap is wide and the exact value is not delicate.
    private const double Fold = 310;

    /// Left half. Observed anchors: section labels 48.2, a row's date 52.3, the
    /// description indent 83.5, amounts right-aligned to ~285.
    ///
    /// The first bound is what separates a *row* from a *heading*: both start at
    /// the outer margin, but a description and its continuation lines are
    /// indented past it. That is the whole reason "Ways to pay" — the marketing
    /// section printed directly under the last transaction — is not swallowed
    /// into that transaction's description.
    private static readonly double[] LeftBounds = [78, 235];

    /// Right half, the same three columns shifted by the fold: labels 325.4,
    /// dates 330.2, descriptions 361.4, amounts right-aligned to ~563.
    private static readonly double[] RightBounds = [355, 512];

    internal const int ColLabel = 0;
    internal const int ColDescription = 1;
    internal const int ColAmount = 2;

    private static readonly StatementGrid.Band[] Bands =
    [
        new(double.NegativeInfinity, Fold, LeftBounds),
        new(Fold, double.PositiveInfinity, RightBounds),
    ];

    internal static List<string[]> Build(byte[] pdf) => StatementGrid.Build(pdf, Bands);
}
