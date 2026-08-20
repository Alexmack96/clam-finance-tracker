using System.Globalization;
using System.Text.RegularExpressions;

namespace Clam.Api.Features.Import.ImportHsbc;

/// Reads an HSBC current-account statement into rows, and refuses to hand back
/// rows it cannot prove correct.
///
/// The table is four columns — details, £ Paid out, £ Paid in, £ Balance — and
/// **a payment's direction is the column its figure sat in, never its type**.
/// That is the whole reason this is a positional parse: some incoming payments
/// are typed BP rather than CR, so flattening the page to text and reading the
/// type would book money in the wrong direction.
///
/// One transaction spans several printed lines. A line starting with a payment
/// type opens a new one, its continuation lines add to the description, and the
/// figures usually print on the *last* of those lines. Statements run over
/// several pages, each closing with a running BALANCE CARRIED FORWARD: those
/// flush the transaction in hand but never end the parse, because the next
/// page's transactions follow.
public static partial class HsbcStatementParser
{
    public static HsbcParseResult Parse(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        List<string[]> grid;
        try
        {
            grid = HsbcStatementGrid.Build(pdf);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return HsbcParseResult.Rejected(400, "Failed to extract text from PDF");
        }

        var guard = CheckIsHsbcStatement(grid);
        if (guard is not null) return HsbcParseResult.Rejected(422, guard);

        var period = ReadStatementPeriod(grid);
        if (period is null)
            return HsbcParseResult.Rejected(400, "Could not find the statement period in the PDF");

        var rows = ReadRows(grid, period.Value.Label, period.Value.Year);
        var mismatch = Reconcile(rows, grid);

        return mismatch is not null
            ? HsbcParseResult.Rejected(422, mismatch)
            : HsbcParseResult.Parsed(rows, period.Value.Label);
    }

    // ── Is this even an HSBC statement? ──────────────────────────────────────

    /// The article is stored rather than derived. A first-letter vowel test gets
    /// "an HSBC statement" wrong, because the rule is about how the name is said,
    /// not how it is spelled.
    private static readonly (string Article, string Label, Regex Pattern)[] BankMarkers =
    [
        ("an", "HSBC", HsbcMarker()),
        ("an", "American Express", AmericanExpressMarker()),
        ("a", "Barclays", BarclaysMarker()),
        ("a", "Santander", SantanderMarker()),
        ("a", "Chase", ChaseMarker()),
        ("a", "SoFi", SofiMarker()),
    ];

    private static string? CheckIsHsbcStatement(List<string[]> grid)
    {
        var text = string.Join('\n', grid.Select(line => string.Join('\t', line)));

        if (BankMarkers[0].Pattern.IsMatch(text)) return null;

        var other = BankMarkers.Skip(1).FirstOrDefault(b => b.Pattern.IsMatch(text));
        return other.Label is not null
            ? $"This looks like {other.Article} {other.Label} statement, not HSBC. Upload it under the {other.Label} card instead."
            : "This doesn't look like an HSBC statement — no \"HSBC\" marker found. Did you upload the right bank's file?";
    }

    // ── Statement period ─────────────────────────────────────────────────────

    /// "10 March to 9 April 2026" — the closing year is the one that matters, as
    /// the row dates carry their own two-digit year.
    private static (string Label, int Year)? ReadStatementPeriod(List<string[]> grid)
    {
        foreach (var line in grid)
        {
            var match = PeriodPattern().Match(line[HsbcStatementGrid.ColDetails]);
            if (!match.Success) continue;

            var year = int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture);
            var label = match.Groups[3].Success
                ? $"{match.Groups[1].Value} {match.Groups[2].Value} {match.Groups[3].Value} to {match.Groups[4].Value} {match.Groups[5].Value} {match.Groups[6].Value}"
                : $"{match.Groups[1].Value} {match.Groups[2].Value} to {match.Groups[4].Value} {match.Groups[5].Value} {match.Groups[6].Value}";

            return (label, year);
        }

        return null;
    }

    // ── Rows ─────────────────────────────────────────────────────────────────

    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static List<HsbcRow> ReadRows(List<string[]> grid, string statementDate, int statementYear)
    {
        var rows = new List<HsbcRow>();

        HsbcRow? current = null;
        var description = new List<string>();
        var currentDate = "";

        void Flush()
        {
            if (current is null) return;

            // Description falls back to the payment type: a few rows print
            // nothing but a code, and an empty description is worse than a
            // repeated one, because it is what the business key hashes.
            var text = CollapseWhitespace(string.Join(' ', description));
            rows.Add(current with { Description = text.Length > 0 ? text : current.PaymentType });

            current = null;
            description.Clear();
        }

        foreach (var line in grid)
        {
            var details = line[HsbcStatementGrid.ColDetails];

            // A page-boundary running total. Flush what is in hand, but carry on
            // — page two's transactions come after it.
            if (IsBalanceForwardLine(details))
            {
                Flush();
                continue;
            }

            // A leading date sets the date for this and following rows; the rest
            // of the line is treated exactly like a line with no date at all.
            var dated = DatePattern().Match(details);
            if (dated.Success)
            {
                var month = Array.IndexOf(Months, dated.Groups[2].Value) + 1;
                var day = int.Parse(dated.Groups[1].Value, CultureInfo.InvariantCulture);
                var year = 2000 + int.Parse(dated.Groups[3].Value, CultureInfo.InvariantCulture);
                currentDate = $"{year:D4}-{month:D2}-{day:D2}";

                details = dated.Groups[4].Value.Trim();

                if (IsBalanceForwardLine(details))
                {
                    Flush();
                    continue;
                }
            }

            var typed = PaymentTypePattern().Match(details);
            if (typed.Success)
            {
                Flush();
                current = new HsbcRow
                {
                    Date = currentDate,
                    PaymentType = typed.Groups[1].Value,
                    Description = "",
                    StatementDate = statementDate,
                };

                var remainder = typed.Groups[2].Value.Trim();
                if (remainder.Length > 0) description.Add(remainder);
            }
            else if (current is not null && details.Length > 0 && !IsNoise(details))
            {
                description.Add(details);
            }

            // Figures belong to whatever transaction is open, whichever of its
            // lines they were printed on. Last one wins, matching the statement:
            // a transaction never prints two figures in the same column.
            if (current is null) continue;

            var paidOut = Money(line[HsbcStatementGrid.ColPaidOut]);
            var paidIn = Money(line[HsbcStatementGrid.ColPaidIn]);
            var balance = Money(line[HsbcStatementGrid.ColBalance]);

            if (paidOut is not null) current.MoneyOut = paidOut;
            if (paidIn is not null) current.MoneyIn = paidIn;
            if (balance is not null) current.Balance = balance;
        }

        Flush();
        return rows;
    }

    // ── Reconciliation ───────────────────────────────────────────────────────

    /// Page one's Account Summary:
    ///
    ///     Opening Balance + Payments In − Payments Out = Closing Balance
    ///
    /// The summary's own arithmetic is checked first — if that does not hold we
    /// read the wrong boxes and its totals mean nothing — and then the parsed
    /// rows must sum to the same two totals.
    ///
    /// The TypeScript importer never had this. Its comment claimed a
    /// `reconcileHsbc` that was never written, so a dropped or mis-read row
    /// imported silently. That is the failure this exists to make impossible.
    private static string? Reconcile(List<HsbcRow> rows, List<string[]> grid)
    {
        var summary = ReadAccountSummary(grid);
        if (summary is null)
            return "Couldn't find the Account Summary, so the parse can't be reconciled — nothing was imported.";

        var (opening, paymentsIn, paymentsOut, closing) = summary.Value;

        if (Pence(opening) + Pence(paymentsIn) - Pence(paymentsOut) != Pence(closing))
            return $"The Account Summary didn't add up (£{opening} + £{paymentsIn} − £{paymentsOut} ≠ £{closing}), so it was misread — nothing was imported.";

        foreach (var (label, sum, stated) in new[]
                 {
                     ("paid in", rows.Sum(r => Pence(r.MoneyIn)), paymentsIn),
                     ("paid out", rows.Sum(r => Pence(r.MoneyOut)), paymentsOut),
                 })
        {
            if (sum != Pence(stated))
                return $"Parsed {label} total {Gbp(sum)} but the statement's Account Summary says £{stated}. The PDF didn't parse cleanly — nothing was imported.";
        }

        return null;
    }

    /// The four figures print in the balance column, but the box itself is set
    /// to the right of the page — far enough right that its *labels* land in the
    /// paid-out column, not the details one. So the label is read from the whole
    /// line rather than from a fixed cell.
    ///
    /// The labels are also letter-spaced in the PDF — "Ope ning Balance" — so
    /// they are matched with the whitespace squashed out rather than literally,
    /// which no amount of regex tolerance would survive.
    private static (string Opening, string In, string Out, string Closing)? ReadAccountSummary(List<string[]> grid)
    {
        string? opening = null, paymentsIn = null, paymentsOut = null, closing = null;

        foreach (var line in grid)
        {
            var label = Squash(string.Concat(line[..HsbcStatementGrid.ColBalance]));
            var value = Money(line[HsbcStatementGrid.ColBalance].TrimStart('£'));
            if (value is null) continue;

            if (label.StartsWith("OpeningBalance", StringComparison.OrdinalIgnoreCase)) opening ??= value;
            else if (label.StartsWith("PaymentsIn", StringComparison.OrdinalIgnoreCase)) paymentsIn ??= value;
            else if (label.StartsWith("PaymentsOut", StringComparison.OrdinalIgnoreCase)) paymentsOut ??= value;
            else if (label.StartsWith("ClosingBalance", StringComparison.OrdinalIgnoreCase)) closing ??= value;
        }

        return opening is not null && paymentsIn is not null && paymentsOut is not null && closing is not null
            ? (opening, paymentsIn, paymentsOut, closing)
            : null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsBalanceForwardLine(string details) =>
        BalanceForwardPattern().IsMatch(Squash(details));

    /// Page furniture that sits inside the table's own column and would
    /// otherwise be appended to whatever transaction is open. Squashed, because
    /// the headers are letter-spaced.
    private static readonly string[] NoisePrefixes =
    [
        "DatePayment", "AccountSummary", "AccountName", "YourHSBC", "Contacttel",
        "Textphone", "www.", "InternationalBank", "BankIdentifier", "Sortcode",
        "YourPremier", "Informationabout", "Yourdeposit",
    ];

    private static bool IsNoise(string details)
    {
        var squashed = Squash(details);
        return NoisePrefixes.Any(p => squashed.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// A figure standing alone in a money column: 1-3 digits, optional
    /// comma-thousands groups, two decimals. Reference numbers never match.
    private static string? Money(string cell)
    {
        var trimmed = cell.Trim();
        return MoneyPattern().IsMatch(trimmed) ? trimmed : null;
    }

    private static string Squash(string value) => WhitespacePattern().Replace(value, "");

    private static string CollapseWhitespace(string value) =>
        WhitespaceRunPattern().Replace(value, " ").Trim();

    private static long Pence(string? printed)
    {
        if (printed is null) return 0;
        var value = decimal.Parse(printed.Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        return (long)decimal.Round(value * 100, 0, MidpointRounding.AwayFromZero);
    }

    private static string Gbp(long pence) =>
        (pence / 100m).ToString("£#,##0.00", CultureInfo.GetCultureInfo("en-GB"));

    [GeneratedRegex(@"^(\d{2})\s+(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+(\d{2})\s*(.*)")]
    private static partial Regex DatePattern();

    /// The code must be a whole token. The TypeScript original allowed it to run
    /// straight into the next word, which turns the interest-rates footer's
    /// "Cre dit inte re s t" into a CR transaction — a row invented out of page
    /// furniture.
    [GeneratedRegex(@"^(OBP|BGC|ATM|CHQ|DEB|STO|TFR|VIS|BP|CR|DD|SO|FP|DR)(?:\s+(.*))?$")]
    private static partial Regex PaymentTypePattern();

    [GeneratedRegex(@"(\d{1,2})\s+(\w+)(?:\s+(\d{4}))?\s+to\s+(\d{1,2})\s+(\w+)\s+(\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodPattern();

    [GeneratedRegex(@"^\d{1,3}(?:,\d{3})*\.\d{2}$")]
    private static partial Regex MoneyPattern();

    [GeneratedRegex("BALANCE(BROUGHT|CARRIED)FORWARD", RegexOptions.IgnoreCase)]
    private static partial Regex BalanceForwardPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunPattern();

    [GeneratedRegex(@"\bHSBC\b", RegexOptions.IgnoreCase)]
    private static partial Regex HsbcMarker();

    [GeneratedRegex("American Express|Membership Rewards", RegexOptions.IgnoreCase)]
    private static partial Regex AmericanExpressMarker();

    [GeneratedRegex(@"\bBarclay", RegexOptions.IgnoreCase)]
    private static partial Regex BarclaysMarker();

    [GeneratedRegex(@"\bSantander\b", RegexOptions.IgnoreCase)]
    private static partial Regex SantanderMarker();

    [GeneratedRegex(@"\bChase\b|JPMorgan", RegexOptions.IgnoreCase)]
    private static partial Regex ChaseMarker();

    [GeneratedRegex(@"\bSoFi\b", RegexOptions.IgnoreCase)]
    private static partial Regex SofiMarker();
}
