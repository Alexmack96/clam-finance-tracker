using System.Globalization;
using System.Text.RegularExpressions;

namespace Clam.Api.Features.Import.ImportSantander;

/// Reads a Santander current-account statement PDF into rows, and refuses to
/// hand back rows it cannot prove correct.
///
/// Three things about this statement drive the parse.
///
/// **An entry's direction is the column its figure sat in**, Money in or Money
/// out — never anything in its wording. "DIRECT DEBIT PAYMENT TO" and "FASTER
/// PAYMENTS RECEIPT" happen to read the way they point, and "REGULAR TRANSFER
/// FROM" does not always, so reading the words instead of the column books
/// money backwards on exactly the rows that matter most.
///
/// **Every row prints a running balance.** That is worth more here than it is on
/// any of the other statements, because it makes each row checkable on its own:
/// the balance a row moved to, less the balance before it, has to be the figure
/// in the column the row was read from. A figure read out of the wrong column,
/// or a row dropped entirely, breaks that identity at the row it happened on
/// rather than at the foot of the statement.
///
/// **A long description wraps around its own row.** Santander sets the
/// description over three printed lines and centres the date and the figures
/// against the middle one, so a row's own anchor arrives *between* two lines of
/// its description. Lines are therefore grouped by how far apart they were
/// printed — a wrapped line sits ~4pt from its neighbour, separate entries
/// ~9.5pt — which is why this parser reads positions and not just cells.
///
/// The Express parser this replaces derived direction from the *balance
/// difference* rather than from the column, having no coordinates to read. That
/// is a sound trick and it is kept here as the check rather than the source: the
/// column says what happened, and the balance proves it.
public static partial class SantanderStatementParser
{
    public static SantanderParseResult Parse(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        List<StatementGrid.GridRow> grid;
        try
        {
            grid = SantanderStatementGrid.Build(pdf);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SantanderParseResult.Rejected(400, "Failed to extract text from PDF");
        }

        var guard = CheckIsSantanderStatement(grid);
        if (guard is not null) return SantanderParseResult.Rejected(422, guard);

        var period = ReadPeriod(grid);
        if (period is null)
            return SantanderParseResult.Rejected(400, "Could not find the statement period in the PDF");

        var read = ReadEntries(grid, period.Value);
        if (!read.FoundTransactions)
            return SantanderParseResult.Rejected(400, "Could not find the \"Your transactions\" section in the PDF");

        var mismatch = Reconcile(read, ReadSummary(grid));
        return mismatch is not null
            ? SantanderParseResult.Rejected(422, mismatch)
            : SantanderParseResult.Parsed(read.Rows, period.Value.Label);
    }

    // ── Is this even a Santander statement? ──────────────────────────────────

    /// The article is stored rather than derived. A first-letter vowel test gets
    /// "an HSBC statement" wrong, because the rule is about how the name is said,
    /// not how it is spelled.
    private static readonly (string Article, string Label, Regex Pattern)[] BankMarkers =
    [
        ("a", "Santander", SantanderMarker()),
        ("an", "American Express", AmericanExpressMarker()),
        ("a", "Barclaycard", BarclaysMarker()),
        ("an", "HSBC", HsbcMarker()),
        ("a", "Chase", ChaseMarker()),
        ("a", "SoFi", SofiMarker()),
    ];

    private static string? CheckIsSantanderStatement(List<StatementGrid.GridRow> grid)
    {
        var text = string.Join('\n', grid.Select(line => string.Join('\t', line.Cells)));

        if (BankMarkers[0].Pattern.IsMatch(text)) return null;

        var other = BankMarkers.Skip(1).FirstOrDefault(b => b.Pattern.IsMatch(text));
        return other.Label is not null
            ? $"This looks like {other.Article} {other.Label} statement, not Santander. Upload it under the {other.Label} account instead."
            : "This doesn't look like a Santander statement — no \"Santander\" marker found. Did you upload the right bank's file?";
    }

    // ── What period does it cover? ───────────────────────────────────────────

    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// The label is built without the ordinal suffixes — "21 Jan 2026 to 20 Feb
    /// 2026" — matching what the Express importer stored, so the same statement
    /// carries the same <c>statementDate</c> in both.
    ///
    /// The end of the period is what dates are resolved against, not the start:
    /// a statement running 22nd Dec 2025 to 20th Jan 2026 prints its December
    /// entries as "22nd Dec", and only the end tells you which year that is.
    private static (string Label, int EndMonth, int EndYear)? ReadPeriod(List<StatementGrid.GridRow> grid)
    {
        foreach (var line in grid)
        {
            var match = PeriodPattern().Match(Joined(line));
            if (!match.Success) continue;

            var endMonth = MonthOf(match.Groups[5].Value);
            if (endMonth == 0) continue;

            var label = $"{match.Groups[1].Value} {match.Groups[2].Value} {match.Groups[3].Value}"
                + $" to {match.Groups[4].Value} {match.Groups[5].Value} {match.Groups[6].Value}";

            return (label, endMonth, int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture));
        }

        return null;
    }

    private static int MonthOf(string name) => Array.FindIndex(
        Months,
        m => name.StartsWith(m, StringComparison.OrdinalIgnoreCase)) + 1;

    // ── What the statement says about itself ─────────────────────────────────

    /// The four figures the front page prints, all optional because a misread
    /// page is exactly the case where one goes missing.
    ///
    /// Read with unanchored patterns off the whole joined line, deliberately.
    /// The front page sets a marketing column down its right-hand side, and a
    /// line of it that shares a baseline with a summary line is joined onto the
    /// end of it. Anchoring would make the parse depend on where a "Stay one
    /// step ahead of fraudsters" happened to fall.
    private sealed record Summary(string? Opening, string? TotalIn, string? TotalOut, string? Closing);

    private static Summary ReadSummary(List<StatementGrid.GridRow> grid)
    {
        string? opening = null, totalIn = null, totalOut = null, closing = null;

        foreach (var line in grid)
        {
            var joined = Joined(line);
            opening ??= Captured(SummaryOpeningPattern(), joined);
            totalIn ??= Captured(SummaryTotalInPattern(), joined);
            totalOut ??= Captured(SummaryTotalOutPattern(), joined);
            closing ??= Captured(SummaryClosingPattern(), joined);
        }

        return new Summary(opening, totalIn, totalOut, closing);
    }

    private static string? Captured(Regex pattern, string text)
    {
        var match = pattern.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    // ── Entries ──────────────────────────────────────────────────────────────

    private sealed record Read
    {
        public List<SantanderRow> Rows { get; } = [];
        public bool FoundTransactions { get; set; }

        /// Off the transactions table's own first row, not the front page's
        /// summary. The two are checked against each other during reconciliation.
        public string? Opening { get; set; }

        /// The last balance the table printed, whether that came from a "Balance
        /// carried forward" line or simply from the final entry. The February
        /// statement prints no carried-forward line at all.
        public string? Closing { get; set; }

        /// The row whose figure disagreed with the balance it moved to, if any.
        /// Reported by the reconciliation rather than thrown, so the message can
        /// name the entry.
        public string? RowMismatch { get; set; }
    }

    /// Lines printed closer together than this belong to one wrapped entry.
    /// Consecutive entries are ~9.5pt apart and the wrapped lines of one entry
    /// ~4pt, so the threshold sits in a gap rather than on a cliff edge.
    private const double WrapTolerance = 6;

    private static Read ReadEntries(List<StatementGrid.GridRow> grid, (string Label, int EndMonth, int EndYear) period)
    {
        var read = new Read();
        var section = new List<StatementGrid.GridRow>();

        foreach (var line in grid)
        {
            var joined = Normalise(Joined(line));

            if (!read.FoundTransactions)
            {
                // Off the whole line: "Your transactions" is followed on the same
                // line by the period it covers, and "Your" and "transactions"
                // straddle the date column's bound.
                read.FoundTransactions = joined.StartsWith("Your transactions", StringComparison.Ordinal);
                continue;
            }

            // A continuation page repeats the account header above the table.
            if (PageFurniturePattern().IsMatch(joined)) continue;

            section.Add(line);
        }

        if (!read.FoundTransactions) return read;

        var previousBalance = 0L;
        var haveOpening = false;

        foreach (var block in Blocks(section))
        {
            var date = block.Select(l => l.Cells[SantanderStatementGrid.ColDate]).FirstOrDefault(c => c.Length > 0);
            var description = CollapseWhitespace(string.Join(' ',
                block.Select(l => l.Cells[SantanderStatementGrid.ColDescription]).Where(c => c.Length > 0)));
            var moneyIn = FirstMoney(block, SantanderStatementGrid.ColMoneyIn);
            var moneyOut = FirstMoney(block, SantanderStatementGrid.ColMoneyOut);
            var balance = FirstMoney(block, SantanderStatementGrid.ColBalance);

            if (BroughtForwardPattern().IsMatch(description))
            {
                if (balance is null) continue;
                read.Opening = balance;
                previousBalance = Pence(balance);
                haveOpening = true;
                continue;
            }

            // The last line of the table. Everything below it is page furniture.
            if (CarriedForwardPattern().IsMatch(description))
            {
                read.Closing = balance ?? read.Closing;
                break;
            }

            if (balance is null)
            {
                // No figure and no balance: either the table's own column
                // heading, or a description that wrapped across a page break.
                // The first has nothing before it to belong to; the second does.
                if (read.Rows.Count > 0 && description.Length > 0 && date is null)
                {
                    var last = read.Rows[^1];
                    read.Rows[^1] = last with
                    {
                        Description = CollapseWhitespace($"{last.Description} {description}"),
                    };
                }

                continue;
            }

            if (date is null) continue;

            var iso = IsoDate(date, period.EndMonth, period.EndYear);
            if (iso is null) continue;

            // The row's own proof: the balance it moved to, less the balance
            // before it, is the figure in the column it was read from. Checked
            // before the row is kept so the message can name the entry, and only
            // once the opening balance is known — without it every row after
            // would fail for one row's sake.
            if (haveOpening && read.RowMismatch is null)
            {
                var moved = Pence(balance) - previousBalance;
                var figure = moneyIn is not null ? Pence(moneyIn)
                    : moneyOut is not null ? -Pence(moneyOut)
                    : 0L;

                if (moved != figure)
                {
                    read.RowMismatch =
                        $"\"{description}\" on {iso} moved the balance by {Gbp(moved)} but its Money "
                        + $"{(moneyIn is not null ? "in" : "out")} column reads {Gbp(Math.Abs(figure))}.";
                }
            }

            previousBalance = Pence(balance);
            read.Closing = balance;

            read.Rows.Add(new SantanderRow
            {
                Date = iso,
                Description = description,
                MoneyIn = moneyIn,
                MoneyOut = moneyOut,
                Balance = balance,
                StatementDate = period.Label,
            });
        }

        return read;
    }

    /// Consecutive lines of one printed entry, grouped by how close together
    /// they were set. Page is part of the comparison: y restarts at the top of
    /// each page, so the last line of one page and the first of the next have no
    /// meaningful distance between them.
    private static List<List<StatementGrid.GridRow>> Blocks(List<StatementGrid.GridRow> lines)
    {
        var blocks = new List<List<StatementGrid.GridRow>>();

        foreach (var line in lines)
        {
            var previous = blocks.Count > 0 ? blocks[^1][^1] : (StatementGrid.GridRow?)null;

            if (previous is { } p && p.Page == line.Page && p.Y - line.Y <= WrapTolerance)
                blocks[^1].Add(line);
            else
                blocks.Add([line]);
        }

        return blocks;
    }

    private static string? FirstMoney(List<StatementGrid.GridRow> block, int column) =>
        block.Select(l => Money(l.Cells[column])).FirstOrDefault(m => m is not null);

    // ── Does it add up? ──────────────────────────────────────────────────────

    /// Two independent checks, and the statement is refused unless both hold.
    ///
    /// The per-row balance identity catches a figure read out of the wrong
    /// column or a row dropped mid-table. The totals catch what it cannot: a row
    /// dropped from the *end* of the table, where nothing after it disagrees.
    private static string? Reconcile(Read read, Summary summary)
    {
        if (read.RowMismatch is not null)
            return read.RowMismatch + " The PDF didn't parse cleanly — nothing was imported.";

        if (summary.Opening is null || summary.TotalIn is null
            || summary.TotalOut is null || summary.Closing is null)
        {
            return "Couldn't find all of the statement's own totals, so the parse can't be reconciled — nothing was imported.";
        }

        if (read.Opening is null)
            return "The transactions table has no opening balance, so the parse can't be reconciled — nothing was imported.";

        if (Pence(read.Opening) != Pence(summary.Opening))
        {
            return $"The table opens at £{read.Opening} but the summary says £{summary.Opening}, "
                + "so one of them was misread — nothing was imported.";
        }

        if (Pence(summary.Opening) + Pence(summary.TotalIn) - Pence(summary.TotalOut) != Pence(summary.Closing))
        {
            return $"The statement's own summary didn't add up (£{summary.Opening} + £{summary.TotalIn} − £{summary.TotalOut} ≠ £{summary.Closing}), "
                + "so it was misread — nothing was imported.";
        }

        if (read.Closing is null || Pence(read.Closing) != Pence(summary.Closing))
        {
            return $"The table closes at £{read.Closing ?? "nothing"} but the summary says £{summary.Closing}. "
                + "The PDF didn't parse cleanly — nothing was imported.";
        }

        var paidIn = read.Rows.Where(r => r.MoneyIn is not null).Sum(r => Pence(r.MoneyIn!));
        if (paidIn != Pence(summary.TotalIn))
            return $"Parsed money in {Gbp(paidIn)} but the statement says £{summary.TotalIn}. The PDF didn't parse cleanly — nothing was imported.";

        var paidOut = read.Rows.Where(r => r.MoneyOut is not null).Sum(r => Pence(r.MoneyOut!));
        if (paidOut != Pence(summary.TotalOut))
            return $"Parsed money out {Gbp(paidOut)} but the statement says £{summary.TotalOut}. The PDF didn't parse cleanly — nothing was imported.";

        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Joined(StatementGrid.GridRow line) =>
        string.Join(' ', line.Cells.Where(c => c.Length > 0));

    /// "21st Jan" as printed, to an ISO date. The year comes from the statement
    /// period's end: a month later in the year than the period ended in belongs
    /// to the year before.
    private static string? IsoDate(string cell, int endMonth, int endYear)
    {
        var match = EntryDatePattern().Match(cell.Trim());
        if (!match.Success) return null;

        var month = MonthOf(match.Groups[2].Value);
        if (month == 0) return null;

        var day = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var year = month > endMonth ? endYear - 1 : endYear;

        return $"{year:D4}-{month:D2}-{day:D2}";
    }

    /// The amount as printed. Only a properly formatted figure counts: the money
    /// columns are otherwise empty, and the heading cells ("Money in", "£
    /// Balance") sit in them too.
    private static string? Money(string cell)
    {
        var match = MoneyPattern().Match(cell.Trim());
        return match.Success ? match.Groups[1].Value : null;
    }

    /// Whitespace squashed to single spaces and curly apostrophes flattened, so
    /// a heading can be compared literally.
    private static string Normalise(string value) =>
        CollapseWhitespace(value).Replace('’', '\'');

    private static string CollapseWhitespace(string value) =>
        WhitespaceRunPattern().Replace(value, " ").Trim();

    private static long Pence(string printed)
    {
        var value = decimal.Parse(printed.Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        return (long)decimal.Round(value * 100, 0, MidpointRounding.AwayFromZero);
    }

    /// Match the statement's own formatting so the two figures in an error
    /// message can be read side by side.
    private static string Gbp(long pence) =>
        (pence / 100m).ToString("£#,##0.00", CultureInfo.GetCultureInfo("en-GB"));

    [GeneratedRegex(@"^(\d{1,2})(?:st|nd|rd|th)\s+([A-Za-z]{3,})$")]
    private static partial Regex EntryDatePattern();

    [GeneratedRegex(@"(\d{1,2})(?:st|nd|rd|th)\s+([A-Za-z]{3,})\s+(\d{4})\s+to\s+(\d{1,2})(?:st|nd|rd|th)\s+([A-Za-z]{3,})\s+(\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodPattern();

    [GeneratedRegex(@"Balance brought forward from .*?£([\d,]+\.\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex SummaryOpeningPattern();

    [GeneratedRegex(@"Total money in:\s*£([\d,]+\.\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex SummaryTotalInPattern();

    [GeneratedRegex(@"Total money out:\s*-?£([\d,]+\.\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex SummaryTotalOutPattern();

    [GeneratedRegex(@"balance at close of business.*?£([\d,]+\.\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex SummaryClosingPattern();

    [GeneratedRegex(@"^Balance brought forward", RegexOptions.IgnoreCase)]
    private static partial Regex BroughtForwardPattern();

    [GeneratedRegex(@"^Balance carried forward", RegexOptions.IgnoreCase)]
    private static partial Regex CarriedForwardPattern();

    [GeneratedRegex(@"^(Account (name|number)|Statement number|Page number):", RegexOptions.IgnoreCase)]
    private static partial Regex PageFurniturePattern();

    [GeneratedRegex(@"^([\d,]+\.\d{2})$")]
    private static partial Regex MoneyPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunPattern();

    [GeneratedRegex(@"\bSantander\b", RegexOptions.IgnoreCase)]
    private static partial Regex SantanderMarker();

    [GeneratedRegex("American Express|Membership Rewards", RegexOptions.IgnoreCase)]
    private static partial Regex AmericanExpressMarker();

    [GeneratedRegex(@"\bBarclay", RegexOptions.IgnoreCase)]
    private static partial Regex BarclaysMarker();

    [GeneratedRegex(@"\bHSBC\b", RegexOptions.IgnoreCase)]
    private static partial Regex HsbcMarker();

    [GeneratedRegex(@"\bChase\b|JPMorgan", RegexOptions.IgnoreCase)]
    private static partial Regex ChaseMarker();

    [GeneratedRegex(@"\bSoFi\b", RegexOptions.IgnoreCase)]
    private static partial Regex SofiMarker();
}
