using System.Globalization;
using System.Text.RegularExpressions;

namespace Clam.Api.Features.Import.ImportChase;

/// Reads a Chase credit-card statement PDF into rows, and refuses to hand back
/// rows it cannot prove correct.
///
/// **A row is a line that starts with a date.** Everything else in the activity
/// list is detail hanging off the row above it: a foreign charge prints its
/// posting date, its currency and its exchange rate on two further lines,
/// indented into the description column. Those lines are skipped rather than
/// merged, which is what the Express parser did and is right for a different
/// reason than it had — the figures on them are a foreign amount and a rate, and
/// there is nowhere in <c>ChaseTransactions</c> to put either.
///
/// **The sign on the figure is the direction.** Credits print negative, and they
/// print negative wherever they appear, so a refund under PURCHASE is read as a
/// credit rather than as spending. The section is used for reconciliation, not
/// for direction — the opposite of the Barclaycard statement, which signs
/// nothing and leaves the section as the only evidence.
///
/// Nothing is handed back until the statement's own arithmetic has been checked
/// against it: the Account Summary has to hold on its own, and each activity
/// section has to sum to the summary line that names it.
public static partial class ChaseStatementParser
{
    public static ChaseParseResult Parse(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        List<string[]> grid;
        try
        {
            grid = ChaseStatementGrid.Build(pdf);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ChaseParseResult.Rejected(400, "Failed to extract text from PDF");
        }

        var guard = CheckIsChaseStatement(grid);
        if (guard is not null) return ChaseParseResult.Rejected(422, guard);

        var closing = ReadClosingDate(grid);
        if (closing is null)
            return ChaseParseResult.Rejected(400, "Could not find the closing date in the PDF");

        var read = ReadEntries(grid, closing.Value);
        if (!read.FoundActivity)
            return ChaseParseResult.Rejected(400, "Could not find the \"Account Activity\" section in the PDF");

        var mismatch = Reconcile(read, ReadSummary(grid));
        return mismatch is not null
            ? ChaseParseResult.Rejected(422, mismatch + Diagnose(pdf))
            : ChaseParseResult.Parsed([.. read.Entries.Select(e => e.Row)], closing.Value.Label);
    }

    // ── Is this even a Chase statement? ──────────────────────────────────────

    /// The article is stored rather than derived. A first-letter vowel test gets
    /// "an HSBC statement" wrong, because the rule is about how the name is said,
    /// not how it is spelled.
    private static readonly (string Article, string Label, Regex Pattern)[] BankMarkers =
    [
        ("a", "Chase", ChaseMarker()),
        ("an", "American Express", AmericanExpressMarker()),
        ("a", "Barclaycard", BarclaysMarker()),
        ("an", "HSBC", HsbcMarker()),
        ("a", "Santander", SantanderMarker()),
        ("a", "SoFi", SofiMarker()),
    ];

    private static string? CheckIsChaseStatement(List<string[]> grid)
    {
        var text = string.Join('\n', grid.Select(line => string.Join('\t', line)));

        if (BankMarkers[0].Pattern.IsMatch(text)) return null;

        var other = BankMarkers.Skip(1).FirstOrDefault(b => b.Pattern.IsMatch(text));
        return other.Label is not null
            ? $"This looks like {other.Article} {other.Label} statement, not Chase. Upload it under the {other.Label} card instead."
            : "This doesn't look like a Chase statement — no \"Chase\" marker found. Did you upload the right bank's file?";
    }

    // ── When did the period close? ───────────────────────────────────────────

    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// "Opening/Closing Date 01/23/26 - 02/22/26" on the front page, falling back
    /// to the "Statement Date: 02/22/26" in the page footer. The label kept is
    /// the month and year — "Feb 2026" — matching what the Express importer
    /// stored, so the same statement carries the same <c>statementDate</c> in
    /// both.
    private static (string Label, int Month, int Year)? ReadClosingDate(List<string[]> grid)
    {
        foreach (var line in grid)
        {
            var match = ClosingDatePattern().Match(Joined(line));
            if (!match.Success) continue;

            var month = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (month is < 1 or > 12) continue;

            var year = 2000 + int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            return ($"{Months[month - 1]} {year}", month, year);
        }

        return null;
    }

    // ── Entries ──────────────────────────────────────────────────────────────

    /// Which of the Account Summary's lines an entry has to add up to. Tracked
    /// per entry rather than only as a flag so each section can be reconciled
    /// against its own printed total — an entry that drifted from one section
    /// into the other changes neither the row count nor the overall balance, and
    /// only a per-section check notices.
    private enum Section
    {
        /// Inside Account Activity but above any section heading.
        None,

        Credits,
        Purchases,
    }

    private sealed record Entry(ChaseRow Row, Section Section, long Signed);

    /// The eight figures the Account Summary prints, all optional because a
    /// misread page is exactly the case where one goes missing.
    private sealed record Summary
    {
        public string? PreviousBalance { get; set; }
        public string? Credits { get; set; }
        public string? Purchases { get; set; }
        public string? CashAdvances { get; set; }
        public string? BalanceTransfers { get; set; }
        public string? Fees { get; set; }
        public string? Interest { get; set; }
        public string? NewBalance { get; set; }
    }

    private sealed record Read(List<Entry> Entries, bool FoundActivity);

    /// The two headings these statements print. A section this does not know
    /// about is not guessed at: its entries land in <see cref="Section.None"/>
    /// and the parse is refused, which is a loud failure the first time a fee or
    /// a cash advance appears rather than a quiet mis-booking.
    private static readonly (string Heading, Section Section)[] Headings =
    [
        ("PAYMENTS AND OTHER CREDITS", Section.Credits),
        ("PURCHASE", Section.Purchases),
    ];

    private static Read ReadEntries(List<string[]> grid, (string Label, int Month, int Year) closing)
    {
        var entries = new List<Entry>();
        var started = false;
        var section = Section.None;

        foreach (var line in grid)
        {
            var joined = Undouble(Normalise(Joined(line)));

            if (!started)
            {
                started = joined == "ACCOUNT ACTIVITY";
                continue;
            }

            var heading = Array.FindIndex(Headings, h => h.Heading == joined);
            if (heading >= 0)
            {
                section = Headings[heading].Section;
                continue;
            }

            var iso = IsoDate(line[ChaseStatementGrid.ColDate], closing.Month, closing.Year);
            if (iso is null) continue;

            var printed = Money(line[ChaseStatementGrid.ColAmount]);
            if (printed is null) continue;

            var description = CollapseWhitespace(line[ChaseStatementGrid.ColDescription]);
            if (description.Length == 0) continue;

            var signed = Pence(printed);

            entries.Add(new Entry(
                new ChaseRow
                {
                    Date = iso,
                    Description = description,
                    // Unsigned, and without the comma grouping: this is what the
                    // process step feeds to the FX conversion, and a signed
                    // figure would come back as a negative expense.
                    Amount = Math.Abs(signed / 100m).ToString("0.00", CultureInfo.InvariantCulture),
                    IsCredit = signed < 0,
                    StatementDate = closing.Label,
                },
                section,
                signed));
        }

        return new Read(entries, started);
    }

    private static Summary ReadSummary(List<string[]> grid)
    {
        var summary = new Summary();

        foreach (var line in grid)
        {
            var joined = Normalise(Joined(line));

            summary.PreviousBalance ??= Captured(PreviousBalancePattern(), joined);
            summary.Credits ??= Captured(CreditsPattern(), joined);
            summary.Purchases ??= Captured(PurchasesPattern(), joined);
            summary.CashAdvances ??= Captured(CashAdvancesPattern(), joined);
            summary.BalanceTransfers ??= Captured(BalanceTransfersPattern(), joined);
            summary.Fees ??= Captured(FeesPattern(), joined);
            summary.Interest ??= Captured(InterestPattern(), joined);
            summary.NewBalance ??= Captured(NewBalancePattern(), joined);
        }

        return summary;
    }

    private static string? Captured(Regex pattern, string text)
    {
        var match = pattern.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    // ── Does it add up? ──────────────────────────────────────────────────────

    private static string? Reconcile(Read read, Summary s)
    {
        if (s.PreviousBalance is null || s.Credits is null || s.Purchases is null
            || s.CashAdvances is null || s.BalanceTransfers is null
            || s.Fees is null || s.Interest is null || s.NewBalance is null)
        {
            return "Couldn't find all of the statement's own totals, so the parse can't be reconciled — nothing was imported.";
        }

        var movements = Pence(s.Credits) + Pence(s.Purchases) + Pence(s.CashAdvances)
            + Pence(s.BalanceTransfers) + Pence(s.Fees) + Pence(s.Interest);

        if (Pence(s.PreviousBalance) + movements != Pence(s.NewBalance))
        {
            return $"The statement's balances didn't add up ({Usd(Pence(s.PreviousBalance))} + {Usd(movements)} ≠ {Usd(Pence(s.NewBalance))}), "
                + "so they were misread — nothing was imported.";
        }

        // An entry printed before the first heading belongs to no total, so no
        // check below would ever see it. That is the one way a row could reach
        // the database unreconciled, so it is refused outright.
        var orphan = read.Entries.Find(e => e.Section == Section.None);
        if (orphan is not null)
        {
            return $"\"{orphan.Row.Description}\" was printed above any section heading, so there is no total to check it against "
                + "— nothing was imported.";
        }

        foreach (var (label, section, stated) in new[]
                 {
                     ("payments and other credits", Section.Credits, s.Credits),
                     ("purchases", Section.Purchases, s.Purchases),
                 })
        {
            var sum = read.Entries.Where(e => e.Section == section).Sum(e => e.Signed);
            if (sum != Pence(stated))
                return $"Parsed {label} total {Usd(sum)} but the statement says {Usd(Pence(stated))}. The PDF didn't parse cleanly — nothing was imported.";
        }

        return null;
    }

    /// Why a statement that read cleanly might still not add up. Asked only
    /// once the reconciliation has already failed, because the answer is usually
    /// the whole of it: Chase flattens some pages to an image, and where such a
    /// page falls in the middle of the activity list its transactions are simply
    /// not in the file as text. Nothing can recover those, so the message says
    /// which page to look at rather than leaving the totals to be puzzled over.
    private static string Diagnose(byte[] pdf)
    {
        List<int> textless;
        try
        {
            textless = StatementGrid.TextlessPages(pdf);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return "";
        }

        if (textless.Count == 0) return "";

        var pages = string.Join(", ", textless);
        return textless.Count == 1
            ? $" Page {pages} of this PDF is an image with no text in it, so any transactions printed on it could not be read."
            : $" Pages {pages} of this PDF are images with no text in them, so any transactions printed on them could not be read.";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Joined(string[] line) =>
        string.Join(' ', line.Where(c => c.Length > 0));

    /// "02/08" as printed, to an ISO date. The year comes from the statement's
    /// closing date: a month later in the year than the statement closed in
    /// belongs to the year before.
    private static string? IsoDate(string cell, int closingMonth, int closingYear)
    {
        var match = EntryDatePattern().Match(cell.Trim());
        if (!match.Success) return null;

        var month = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12 || day is < 1 or > 31) return null;

        var year = month > closingMonth ? closingYear - 1 : closingYear;
        return $"{year:D4}-{month:D2}-{day:D2}";
    }

    /// The amount as printed, sign kept. Only a properly formatted figure
    /// counts: the rewards panel prints "102,922" in the same column and a
    /// pattern without the decimals would read it as a charge.
    private static string? Money(string cell)
    {
        var match = MoneyPattern().Match(cell.Trim());
        return match.Success ? match.Groups[1].Value : null;
    }

    /// Chase prints its section headings twice, one copy over the other, to set
    /// them bold. The second copy is drawn a hair below the first, so the two
    /// never coalesce into one run but do land on the same row, and a heading
    /// arrives as "ACCOUNT ACTIVITYACCOUNT ACTIVITY" — doubled as a whole
    /// phrase, not word by word.
    ///
    /// A string that is exactly its own first half repeated is therefore halved.
    /// Applied only to heading comparisons, never to a description: a two-row
    /// pair of identical merchant names would be silently collapsed into one.
    private static string Undouble(string value)
    {
        if (value.Length == 0 || value.Length % 2 != 0) return value;

        var half = value.Length / 2;
        return value.AsSpan(0, half).SequenceEqual(value.AsSpan(half)) ? value[..half] : value;
    }

    /// Whitespace squashed to single spaces and curly apostrophes flattened, so
    /// a heading can be compared literally.
    private static string Normalise(string value) =>
        CollapseWhitespace(value).Replace('’', '\'');

    private static string CollapseWhitespace(string value) =>
        WhitespaceRunPattern().Replace(value, " ").Trim();

    /// Signed, unlike the sterling banks': Chase prints its credits negative and
    /// its summary lines carry an explicit + or −, and the sign is the whole of
    /// what the reconciliation is checking.
    private static long Pence(string printed)
    {
        var cleaned = printed.Replace(",", "", StringComparison.Ordinal)
            .Replace("$", "", StringComparison.Ordinal)
            .Replace("+", "", StringComparison.Ordinal);

        var value = decimal.Parse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture);
        return (long)decimal.Round(value * 100, 0, MidpointRounding.AwayFromZero);
    }

    /// Match the statement's own formatting so the two figures in an error
    /// message can be read side by side.
    private static string Usd(long cents) =>
        (cents / 100m).ToString("$#,##0.00;-$#,##0.00", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(\d{2})/(\d{2})$")]
    private static partial Regex EntryDatePattern();

    [GeneratedRegex(@"(?:Opening/Closing Date.*?|Statement Date:\s*)(\d{2})/(\d{2})/(\d{2})\s*$")]
    private static partial Regex ClosingDatePattern();

    /// A summary label and its figure are separated by "anything that is not the
    /// start of a figure", not by whitespace: the January statement prints a
    /// stray backtick after "Balance Transfers", and a pattern demanding a clean
    /// space there refused the entire statement over one piece of page furniture.
    [GeneratedRegex(@"^Previous Balance[^\d$+-]*([+-]?\$[\d,]+\.\d{2})")]
    private static partial Regex PreviousBalancePattern();

    [GeneratedRegex(@"^Payment,? Credits[^\d$+-]*([+-]?\$[\d,]+\.\d{2})")]
    private static partial Regex CreditsPattern();

    [GeneratedRegex(@"^Purchases[^\d$+-]*([+-]?\$[\d,]+\.\d{2})")]
    private static partial Regex PurchasesPattern();

    [GeneratedRegex(@"^Cash Advances[^\d$+-]*([+-]?\$[\d,]+\.\d{2})")]
    private static partial Regex CashAdvancesPattern();

    [GeneratedRegex(@"^Balance Transfers[^\d$+-]*([+-]?\$[\d,]+\.\d{2})")]
    private static partial Regex BalanceTransfersPattern();

    [GeneratedRegex(@"^Fees Charged[^\d$+-]*([+-]?\$[\d,]+\.\d{2})")]
    private static partial Regex FeesPattern();

    [GeneratedRegex(@"^Interest Charged[^\d$+-]*([+-]?\$[\d,]+\.\d{2})")]
    private static partial Regex InterestPattern();

    [GeneratedRegex(@"^New Balance[^\d$+-]*([+-]?\$[\d,]+\.\d{2})")]
    private static partial Regex NewBalancePattern();

    [GeneratedRegex(@"^(-?[\d,]+\.\d{2})$")]
    private static partial Regex MoneyPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunPattern();

    [GeneratedRegex(@"\bChase\b|JPMorgan", RegexOptions.IgnoreCase)]
    private static partial Regex ChaseMarker();

    [GeneratedRegex("American Express|Membership Rewards", RegexOptions.IgnoreCase)]
    private static partial Regex AmericanExpressMarker();

    [GeneratedRegex(@"\bBarclay", RegexOptions.IgnoreCase)]
    private static partial Regex BarclaysMarker();

    [GeneratedRegex(@"\bHSBC\b", RegexOptions.IgnoreCase)]
    private static partial Regex HsbcMarker();

    [GeneratedRegex(@"\bSantander\b", RegexOptions.IgnoreCase)]
    private static partial Regex SantanderMarker();

    [GeneratedRegex(@"\bSoFi\b", RegexOptions.IgnoreCase)]
    private static partial Regex SofiMarker();
}
