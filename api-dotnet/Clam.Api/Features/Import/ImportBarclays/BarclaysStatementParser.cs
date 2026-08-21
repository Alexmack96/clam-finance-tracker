using System.Globalization;
using System.Text.RegularExpressions;

namespace Clam.Api.Features.Import.ImportBarclays;

/// Reads a Barclaycard statement PDF into rows, and refuses to hand back rows it
/// cannot prove correct.
///
/// Two things about this statement drive the whole parse.
///
/// **It is set as two magazine columns.** Entries run down the left half of the
/// page and continue at the top of the right half, so y-order interleaves them:
/// read as one table, the 13th of the month arrives between the 22nd and the
/// 24th. <see cref="BarclaysStatementGrid"/> reads each half out in full before
/// starting the next.
///
/// **An entry's direction is the section it was printed under**, never its
/// description. Credits sit under "Payments towards your account" and charges
/// under "How you've used your card"; the TypeScript importer instead tested the
/// description for "Payment By Direct Debit", which is what the credit happens
/// to be called rather than anything the bank guarantees.
///
/// Nothing is handed back until the statement's own arithmetic has been checked
/// against it — twice over, since these statements print both a balance
/// reconciliation and a per-section breakdown, and each catches things the other
/// does not.
public static partial class BarclaysStatementParser
{
    public static BarclaysParseResult Parse(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        List<string[]> grid;
        try
        {
            grid = BarclaysStatementGrid.Build(pdf);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return BarclaysParseResult.Rejected(400, "Failed to extract text from PDF");
        }

        var guard = CheckIsBarclaysStatement(grid);
        if (guard is not null) return BarclaysParseResult.Rejected(422, guard);

        var issued = ReadIssueDate(grid);
        if (issued is null)
            return BarclaysParseResult.Rejected(400, "Could not find the \"issued on\" date in the PDF");

        var (label, month, year) = issued.Value;

        var read = ReadEntries(grid, label, month, year);
        if (!read.FoundTransactions)
            return BarclaysParseResult.Rejected(400, "Could not find the \"Your transactions\" section in the PDF");

        var mismatch = Reconcile(read);
        return mismatch is not null
            ? BarclaysParseResult.Rejected(422, mismatch)
            : BarclaysParseResult.Parsed([.. read.Entries.Select(e => e.Row)], label);
    }

    // ── Is this even a Barclaycard statement? ────────────────────────────────

    /// The article is stored rather than derived. A first-letter vowel test gets
    /// "an HSBC statement" wrong, because the rule is about how the name is said,
    /// not how it is spelled.
    private static readonly (string Article, string Label, Regex Pattern)[] BankMarkers =
    [
        ("a", "Barclays", BarclaysMarker()),
        ("an", "American Express", AmericanExpressMarker()),
        ("an", "HSBC", HsbcMarker()),
        ("a", "Santander", SantanderMarker()),
        ("a", "Chase", ChaseMarker()),
        ("a", "SoFi", SofiMarker()),
    ];

    private static string? CheckIsBarclaysStatement(List<string[]> grid)
    {
        var text = string.Join('\n', grid.Select(line => string.Join('\t', line)));

        if (BankMarkers[0].Pattern.IsMatch(text)) return null;

        var other = BankMarkers.Skip(1).FirstOrDefault(b => b.Pattern.IsMatch(text));
        return other.Label is not null
            ? $"This looks like {other.Article} {other.Label} statement, not Barclaycard. Upload it under the {other.Label} card instead."
            : "This doesn't look like a Barclaycard statement — no \"Barclaycard\" marker found. Did you upload the right bank's file?";
    }

    // ── When was it issued? ──────────────────────────────────────────────────

    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// Every page foots with "Page 1 of 4 // issued on 26 January 2026". Read off
    /// the whole line rather than a cell, because the footer is set across the
    /// right-hand band's column bound and half of it prints in the amount column.
    ///
    /// The label kept is the month and year — "January 2026" — matching what the
    /// Express importer stored, so the same statement carries the same
    /// <c>statementDate</c> in both.
    private static (string Label, int Month, int Year)? ReadIssueDate(List<string[]> grid)
    {
        foreach (var line in grid)
        {
            var match = IssuedOnPattern().Match(string.Join(' ', line));
            if (!match.Success) continue;

            var month = Array.FindIndex(
                Months,
                m => match.Groups[1].Value.StartsWith(m, StringComparison.OrdinalIgnoreCase)) + 1;
            if (month == 0) continue;

            return ($"{match.Groups[1].Value} {match.Groups[2].Value}",
                month,
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        return null;
    }

    // ── Entries ──────────────────────────────────────────────────────────────

    /// Which of the statement's four totals an entry has to add up to. Tracked
    /// per entry rather than just as a credit/debit flag so each section can be
    /// reconciled against its own printed total — an entry that drifted from the
    /// interest section into the spend one changes no total, and only a
    /// per-section check notices.
    private enum Section
    {
        /// Before "Your transactions", or between it and the first heading.
        None,

        Payments,
        Spend,
        Promotional,
        Interest,
    }

    private sealed record Entry(BarclaysRow Row, Section Section);

    /// The seven figures the statement prints about itself, all optional because
    /// a misread page is exactly the case where one goes missing.
    private sealed record Summary
    {
        public string? PreviousBalance { get; set; }
        public string? Payments { get; set; }
        public string? Transactions { get; set; }
        public string? Spend { get; set; }
        public string? Promotional { get; set; }
        public string? Interest { get; set; }
        public string? NewBalance { get; set; }
    }

    private sealed record Read(List<Entry> Entries, Summary Summary, bool FoundTransactions);

    /// The section headings, each of which prints its own total in the amount
    /// column. Matched on the label cell exactly: page 1's "At a glance" box
    /// repeats several of these labels with a trailing colon, and the exactness
    /// is what keeps the two apart even before the "Your transactions" gate.
    private static readonly (string Heading, Section Section)[] Headings =
    [
        ("Payments towards your account", Section.Payments),
        ("How you've used your card", Section.Spend),
        ("Promotional transactions", Section.Promotional),
        ("Interest and charges", Section.Interest),
    ];

    private static Read ReadEntries(List<string[]> grid, string statementDate, int statementMonth, int statementYear)
    {
        var entries = new List<Entry>();
        var summary = new Summary();

        var started = false;
        var section = Section.None;

        string? date = null;
        var description = new List<string>();
        string? amount = null;

        void Flush()
        {
            if (date is not null && amount is not null)
            {
                entries.Add(new Entry(
                    new BarclaysRow
                    {
                        Date = date,
                        Description = CollapseWhitespace(string.Join(' ', description)),
                        Amount = amount,
                        IsCredit = section == Section.Payments,
                        StatementDate = statementDate,
                    },
                    section));
            }

            date = null;
            amount = null;
            description.Clear();
        }

        foreach (var line in grid)
        {
            var label = Normalise(line[BarclaysStatementGrid.ColLabel]);
            var detail = line[BarclaysStatementGrid.ColDescription];
            var money = Money(line[BarclaysStatementGrid.ColAmount]);

            if (!started)
            {
                // Off the whole line, not the label cell: the heading is set
                // large enough that "transactions" starts past the description
                // indent and lands in the next column. Page 1's cross-reference
                // reads "see Your transactions", so the joined line still tells
                // the two apart.
                started = Normalise(string.Join(' ', line)) == "Your transactions";
                continue;
            }

            // A page header closes whatever entry is in hand. Both halves of a
            // page carry entries, and the right half opens with this line, so
            // without it a description would run on across the fold.
            if (PageHeaderPattern().IsMatch(label) || PageHeaderPattern().IsMatch(Normalise(detail)))
            {
                Flush();
                continue;
            }

            var heading = Headings.FirstOrDefault(h => h.Heading == label);
            if (heading.Heading is not null)
            {
                Flush();
                section = heading.Section;
                Record(summary, section, money);
                continue;
            }

            switch (label)
            {
                case "Your previous balance":
                    Flush();
                    summary.PreviousBalance ??= money;
                    continue;
                case "Transactions, interest and charges":
                    Flush();
                    summary.Transactions ??= money;
                    continue;

                // The last line of the section. Everything below it is marketing
                // and legal furniture, on both halves of the page.
                case "Your new balance":
                    Flush();
                    summary.NewBalance ??= money;
                    return new Read(entries, summary, started);
            }

            var dated = EntryDatePattern().Match(label);
            if (dated.Success)
            {
                Flush();
                date = IsoDate(dated, statementMonth, statementYear);
                if (detail.Length > 0) description.Add(detail);
                amount = money;
                continue;
            }

            // Anything else printed at the outer margin is a heading, a caption
            // or a marketing line — never part of an entry. Ending the entry
            // here is what stops "Ways to pay", printed directly beneath the last
            // transaction of a column, being read as its description.
            if (label.Length > 0)
            {
                Flush();
                continue;
            }

            if (date is null) continue;

            // Indented under an entry: a wrapped description, or the currency,
            // rate and fee lines a foreign charge prints beneath itself.
            if (detail.Length > 0) description.Add(detail);
            if (money is not null) amount = money;
        }

        Flush();
        return new Read(entries, summary, started);
    }

    private static void Record(Summary summary, Section section, string? money)
    {
        switch (section)
        {
            case Section.Payments: summary.Payments ??= money; break;
            case Section.Spend: summary.Spend ??= money; break;
            case Section.Promotional: summary.Promotional ??= money; break;
            case Section.Interest: summary.Interest ??= money; break;
            default: break;
        }
    }

    /// A statement runs to the day it is issued, so a row whose month is *after*
    /// the statement's belongs to the year before it — December rows on a
    /// January statement.
    private static string IsoDate(Match dated, int statementMonth, int statementYear)
    {
        var day = int.Parse(dated.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = Array.IndexOf(Months, dated.Groups[2].Value) + 1;
        var year = month > statementMonth ? statementYear - 1 : statementYear;

        return $"{year:D4}-{month:D2}-{day:D2}";
    }

    // ── Reconciliation ───────────────────────────────────────────────────────

    /// The statement states its own totals twice, and both are checked.
    ///
    ///     Your previous balance − Payments + Transactions = Your new balance
    ///     How you've used your card + Promotional + Interest = Transactions
    ///
    /// The first bounds the month; the second splits it by section. Neither is
    /// redundant: the balance identity would still hold if a charge were read
    /// into the wrong section, and the section identity would still hold if the
    /// opening balance were misread.
    ///
    /// Then every parsed entry is summed back against the section total it was
    /// printed under, so a dropped, duplicated or mis-read row cannot pass.
    private static string? Reconcile(Read read)
    {
        var s = read.Summary;

        if (s.PreviousBalance is null || s.Payments is null || s.Transactions is null
            || s.Spend is null || s.Promotional is null || s.Interest is null || s.NewBalance is null)
        {
            return "Couldn't find all of the statement's own totals, so the parse can't be reconciled — nothing was imported.";
        }

        if (Pence(s.PreviousBalance) - Pence(s.Payments) + Pence(s.Transactions) != Pence(s.NewBalance))
        {
            return $"The statement's balances didn't add up (£{s.PreviousBalance} − £{s.Payments} + £{s.Transactions} ≠ £{s.NewBalance}), "
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

        if (Pence(s.Spend) + Pence(s.Promotional) + Pence(s.Interest) != Pence(s.Transactions))
        {
            return $"The statement's sections didn't add up (£{s.Spend} + £{s.Promotional} + £{s.Interest} ≠ £{s.Transactions}), "
                + "so they were misread — nothing was imported.";
        }

        foreach (var (label, section, stated) in new[]
                 {
                     ("payments towards the account", Section.Payments, s.Payments),
                     ("card spending", Section.Spend, s.Spend),
                     ("promotional transactions", Section.Promotional, s.Promotional),
                     ("interest and charges", Section.Interest, s.Interest),
                 })
        {
            var sum = read.Entries.Where(e => e.Section == section).Sum(e => Pence(e.Row.Amount));
            if (sum != Pence(stated))
                return $"Parsed {label} total {Gbp(sum)} but the statement says £{stated}. The PDF didn't parse cleanly — nothing was imported.";
        }

        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// The amount as printed, without its £. The cell can also hold the "e"
    /// contactless marker, which is set in the amount column but a little above
    /// the figure's baseline, so the two share a row.
    private static string? Money(string cell)
    {
        var match = MoneyPattern().Match(cell.Trim());
        return match.Success ? match.Groups[1].Value : null;
    }

    /// Whitespace squashed to single spaces and curly apostrophes flattened, so
    /// a heading can be compared literally. The PDF uses both ' and ’ — "you've"
    /// straight, "you’ll" curly — within the same page.
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

    [GeneratedRegex(@"^(\d{1,2})\s+(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)$")]
    private static partial Regex EntryDatePattern();

    [GeneratedRegex(@"issued on \d{1,2}\s+([A-Za-z]+)\s+(\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex IssuedOnPattern();

    [GeneratedRegex(@"^Page \d+ of \d+ //")]
    private static partial Regex PageHeaderPattern();

    [GeneratedRegex(@"£([\d,]+\.\d{2})$")]
    private static partial Regex MoneyPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunPattern();

    // Barclays / Barclaycard.
    [GeneratedRegex(@"\bBarclay", RegexOptions.IgnoreCase)]
    private static partial Regex BarclaysMarker();

    [GeneratedRegex("American Express|Membership Rewards", RegexOptions.IgnoreCase)]
    private static partial Regex AmericanExpressMarker();

    [GeneratedRegex(@"\bHSBC\b", RegexOptions.IgnoreCase)]
    private static partial Regex HsbcMarker();

    [GeneratedRegex(@"\bSantander\b", RegexOptions.IgnoreCase)]
    private static partial Regex SantanderMarker();

    [GeneratedRegex(@"\bChase\b|JPMorgan", RegexOptions.IgnoreCase)]
    private static partial Regex ChaseMarker();

    [GeneratedRegex(@"\bSoFi\b", RegexOptions.IgnoreCase)]
    private static partial Regex SofiMarker();
}
