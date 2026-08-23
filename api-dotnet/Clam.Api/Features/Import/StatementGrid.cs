using UglyToad.PdfPig;

namespace Clam.Api.Features.Import;

/// Rebuilds a statement PDF as a grid of fixed columns, from the x/y position of
/// every word on the page.
///
/// Reading order is not usable for a statement table. A PDF text extractor emits
/// items in draw order — all the descriptions, then all the amounts — which loses
/// the column and forces the two lists to be re-paired by index. That pairing is
/// what broke the original TypeScript Amex importer: a foreign-spend amount (USD
/// 2.57, in the Foreign Spend column, not the Amount column) was scooped into the
/// amount list and slid every amount on the page down one row, so a TFL charge's
/// £5.35 landed on the next row's Lime hire.
///
/// Grouping by position instead makes a cell's meaning fixed by *where it is
/// printed*, so an amount can never be read as belonging to a different column.
///
/// Feature-shared (tier 2) ahead of the usual rule of three, deliberately. This
/// is mechanical geometry with no business meaning in it — the only thing that
/// varies between banks is where the column boundaries fall, which is the
/// parameter. Two copies of subtle coordinate code is precisely how the two
/// copies drift, and the bug that follows is a silently wrong number rather than
/// a crash. The bank-specific constants stay in the bank's own slice.
internal static class StatementGrid
{
    /// An amount sits ~1pt below its description's baseline; consecutive printed
    /// rows are ~19pt apart. Anything within this of the row's first item is the
    /// same row.
    private const double RowTolerance = 2.5;

    /// Words closer together than this are one printed run.
    ///
    /// Load-bearing, and the one place this differs from the pdf.js original that
    /// preceded it. pdf.js emitted one item per run, so a whole "TICKET NUMBER: …
    /// MR ALEXANDER JAMES MACKI" was classified by where the *run* started.
    /// PdfPig emits words, and "JAMES" starts at x=342.1 — past Amex's 340 bound
    /// — so classifying word by word tears the tail of a description into the
    /// next column, where it is then a candidate to be misread as a value.
    /// Rebuilding runs first restores the original behaviour.
    private const double RunGap = 3;

    /// Below this, two words were printed hard against each other and the space
    /// between them is kerning, not a gap.
    private const double SpaceGap = 0.6;

    /// Two words belong to the same run only if they share a baseline. Compared
    /// with a tolerance rather than for equality: the values are floating point,
    /// out of a matrix multiplication rather than a literal.
    private const double BaselineEpsilon = 0.01;

    /// A vertical slice of the page, read top to bottom in full before the next
    /// slice begins, with its own column bounds.
    ///
    /// Statements that print one table across the page are a single band. A
    /// magazine-style layout — Barclaycard's, where the entries flow down the
    /// left half of the page and continue at the top of the right half — is two,
    /// and reading it as one table is not a near miss but nonsense: y-order
    /// interleaves the two halves, so the 13th of the month lands between the
    /// 22nd and the 24th.
    ///
    /// <param name="Left">Inclusive x at which the band starts.</param>
    /// <param name="Right">Exclusive x at which the band ends.</param>
    /// <param name="UpperBounds">This band's column bounds, in page coordinates.</param>
    internal readonly record struct Band(double Left, double Right, double[] UpperBounds);

    /// One printed line, split into columns, and where on the page it was
    /// printed.
    ///
    /// Most parsers only need <see cref="Cells"/> and take the <c>Build</c>
    /// overloads that hand back just those. The position is for statements whose
    /// rows wrap: Santander sets a long description over three printed lines and
    /// vertically centres the date and figures against the middle one, so the
    /// row's own anchor arrives *between* two lines of its description. Which
    /// lines belong together is then a question about spacing — a wrapped line
    /// sits ~4pt from its neighbour where separate entries sit ~9.5pt apart —
    /// and there is no way to ask it without the coordinate.
    ///
    /// <param name="Page">1-based, and part of the answer: y restarts at the top
    /// of each page, so two lines with near-identical y are adjacent only if they
    /// were printed on the same one.</param>
    internal readonly record struct GridRow(int Page, double Y, string[] Cells);

    /// One printed line per entry, split into columns. A column the line does not
    /// reach is an empty string, never null, so callers can index freely.
    ///
    /// <param name="upperBounds">
    /// Upper x-bound of every column but the last; a word to the right of them
    /// all belongs to the final column. So three bounds produce four columns.
    /// </param>
    internal static List<string[]> Build(byte[] pdf, double[] upperBounds) =>
        BuildRows(pdf, upperBounds).ConvertAll(r => r.Cells);

    internal static List<GridRow> BuildRows(byte[] pdf, double[] upperBounds)
    {
        ArgumentNullException.ThrowIfNull(upperBounds);

        return BuildRows(pdf, [new Band(double.NegativeInfinity, double.PositiveInfinity, upperBounds)]);
    }

    /// The banded form: every band of a page is read out in full before the next
    /// band of that page begins, which is what puts a two-column layout back into
    /// reading order.
    internal static List<string[]> Build(byte[] pdf, Band[] bands) =>
        BuildRows(pdf, bands).ConvertAll(r => r.Cells);

    internal static List<GridRow> BuildRows(byte[] pdf, Band[] bands)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(bands);

        using var stream = new MemoryStream(pdf, writable: false);
        using var document = PdfDocument.Open(stream);

        var lines = new List<GridRow>();
        var pageNumber = 0;
        foreach (var page in document.GetPages())
        {
            pageNumber++;
            var words = page.GetWords()
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .Select(w => new Fragment(
                    w.Text,
                    w.BoundingBox.Left,
                    // The baseline, not the box bottom: a descender ("y", "g")
                    // drops the box and would split one printed line in two.
                    w.Letters[0].StartBaseLine.Y,
                    w.BoundingBox.Right))
                .OrderByDescending(w => w.Y).ThenBy(w => w.Left)
                .ToList();

            // Words are filtered to the band before they are coalesced, so a run
            // can never straddle the fold and be classified into the wrong half's
            // columns.
            foreach (var band in bands)
            {
                var inBand = words.Where(w => w.Left >= band.Left && w.Left < band.Right).ToList();
                AppendPage(lines, pageNumber, Coalesce(inBand), band.UpperBounds);
            }
        }

        return lines;
    }

    /// The 1-based page numbers that carry no text at all — a page that was
    /// scanned or flattened to an image rather than typeset.
    ///
    /// Not a rejection on its own, and deliberately not checked before parsing:
    /// a statement's last page is often a rasterised insert carrying nothing,
    /// and refusing those would refuse statements that read perfectly. It is
    /// asked for only to explain a reconciliation that has already failed, where
    /// it is usually the whole answer — a Chase statement was found with an
    /// image page of transactions sitting between two text ones, worth $2,587.83
    /// that no text parser can see.
    internal static List<int> TextlessPages(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        using var stream = new MemoryStream(pdf, writable: false);
        using var document = PdfDocument.Open(stream);

        var textless = new List<int>();
        var pageNumber = 0;

        foreach (var page in document.GetPages())
        {
            pageNumber++;
            if (page.Letters.Count == 0) textless.Add(pageNumber);
        }

        return textless;
    }

    private static List<Fragment> Coalesce(List<Fragment> words)
    {
        var runs = new List<Fragment>(words.Count);
        foreach (var word in words)
        {
            if (runs.Count > 0)
            {
                var previous = runs[^1];
                if (Math.Abs(previous.Y - word.Y) < BaselineEpsilon && word.Left - previous.Right < RunGap)
                {
                    var separator = word.Left - previous.Right > SpaceGap ? " " : "";
                    runs[^1] = previous with
                    {
                        Text = previous.Text + separator + word.Text,
                        Right = word.Right,
                    };
                    continue;
                }
            }

            runs.Add(word);
        }

        return runs;
    }

    private static void AppendPage(List<GridRow> lines, int page, List<Fragment> runs, double[] upperBounds)
    {
        var columnCount = upperBounds.Length + 1;
        var cells = new string[columnCount];
        var cellEnd = new double[columnCount];
        double? rowY = null;

        void Flush()
        {
            if (rowY is not null)
                lines.Add(new GridRow(page, rowY.Value, [.. cells.Select(c => (c ?? "").Trim())]));
            cells = new string[columnCount];
            cellEnd = new double[columnCount];
        }

        foreach (var run in runs)
        {
            if (rowY is null || rowY - run.Y > RowTolerance)
            {
                Flush();
                rowY = run.Y;
            }

            var column = ColumnOf(run.Left, upperBounds);

            // Only separate runs that are visually apart, so "24" "/" "07" "/"
            // "26" rejoins as "24/07/26" rather than being spaced out.
            var separator = !string.IsNullOrEmpty(cells[column]) && run.Left - cellEnd[column] > 1 ? " " : "";
            cells[column] += separator + run.Text;
            cellEnd[column] = run.Right;
        }

        Flush();
    }

    private static int ColumnOf(double x, double[] upperBounds)
    {
        for (var i = 0; i < upperBounds.Length; i++)
            if (x < upperBounds[i]) return i;
        return upperBounds.Length;
    }

    private readonly record struct Fragment(string Text, double Left, double Y, double Right);
}
