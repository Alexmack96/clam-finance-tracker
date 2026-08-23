using System.Globalization;
using System.Text.RegularExpressions;

namespace Clam.Api.Features.Import.ImportSofi;

/// Reads a SoFi Money statement PDF into rows, and refuses to hand back rows it
/// cannot prove correct.
///
/// **One PDF, two accounts.** Checking is printed in full, then Savings starts
/// over with its own header, its own balances and its own table. Each is
/// reconciled against its own figures, and each row is tagged with the account
/// it came from — which is what lets the process step recognise the transfers
/// between them and skip both sides rather than importing the same dollars
/// twice.
///
/// **Every row prints the balance it moved to, and the rows run newest first.**
/// So each row can be checked against the one printed below it: a row's balance
/// less its amount is the balance the previous entry left, and the oldest row
/// has to land exactly on the Beginning Balance. A figure read out of the wrong
/// column, or a row dropped from the middle of the table, breaks that chain at
/// the row it happened on.
///
/// **The ids are SoFi's own.** See <see cref="SofiBusinessKeys"/>; this is the
/// only statement of the six that gives you one.
public static partial class SofiStatementParser
{
    public static SofiParseResult Parse(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        List<string[]> grid;
        try
        {
            grid = SofiStatementGrid.Build(pdf);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SofiParseResult.Rejected(400, "Failed to extract text from PDF");
        }

        var guard = CheckIsSofiStatement(grid);
        if (guard is not null) return SofiParseResult.Rejected(422, guard);

        var period = ReadPeriod(grid);
        if (period is null)
            return SofiParseResult.Rejected(400, "Could not find the statement period in the PDF");

        var read = ReadAccounts(grid, period);
        if (read.Count == 0)
            return SofiParseResult.Rejected(400, "Could not find a transaction table in the PDF");

        foreach (var account in read)
        {
            var mismatch = Reconcile(account);
            if (mismatch is not null) return SofiParseResult.Rejected(422, mismatch);
        }

        return SofiParseResult.Parsed([.. read.SelectMany(a => a.Rows)], period);
    }

    // ── Is this even a SoFi statement? ───────────────────────────────────────

    /// The article is stored rather than derived. A first-letter vowel test gets
    /// "an HSBC statement" wrong, because the rule is about how the name is said,
    /// not how it is spelled.
    private static readonly (string Article, string Label, Regex Pattern)[] BankMarkers =
    [
        ("a", "SoFi", SofiMarker()),
        ("an", "American Express", AmericanExpressMarker()),
        ("a", "Barclaycard", BarclaysMarker()),
        ("an", "HSBC", HsbcMarker()),
        ("a", "Santander", SantanderMarker()),
        ("a", "Chase", ChaseMarker()),
    ];

    private static string? CheckIsSofiStatement(List<string[]> grid)
    {
        var text = string.Join('\n', grid.Select(line => string.Join('\t', line)));

        if (BankMarkers[0].Pattern.IsMatch(text)) return null;

        var other = BankMarkers.Skip(1).FirstOrDefault(b => b.Pattern.IsMatch(text));
        return other.Label is not null
            ? $"This looks like {other.Article} {other.Label} statement, not SoFi. Upload it under the {other.Label} account instead."
            : "This doesn't look like a SoFi statement — no \"SoFi\" marker found. Did you upload the right bank's file?";
    }

    // ── What period does it cover? ───────────────────────────────────────────

    /// "Feb 1, 2026 - Feb 28, 2026" becomes "Feb 2026" — the end of the period,
    /// matching what the Express importer stored, so the same statement carries
    /// the same <c>statementDate</c> in both.
    private static string? ReadPeriod(List<string[]> grid)
    {
        foreach (var line in grid)
        {
            var match = PeriodPattern().Match(Joined(line));
            if (match.Success) return $"{match.Groups[1].Value} {match.Groups[2].Value}";
        }

        return null;
    }

    // ── Entries ──────────────────────────────────────────────────────────────

    /// One account's half of the statement: its two printed balances and the
    /// rows between them.
    private sealed record Account(string Type)
    {
        public List<SofiRow> Rows { get; } = [];

        /// Where the account stood at the end of the period, off the header.
        public string? CurrentBalance { get; set; }

        /// Where it stood at the start. The oldest row has to land on this.
        public string? BeginningBalance { get; set; }
    }

    private static List<Account> ReadAccounts(List<string[]> grid, string statementDate)
    {
        var accounts = new List<Account>();
        Account? current = null;

        // Set by the "Current Balance" / "Beginning Balance" labels, which print
        // on the line above the figure they label rather than beside it.
        var pending = Label.None;

        foreach (var line in grid)
        {
            var joined = Normalise(Joined(line));

            var heading = AccountHeadingPattern().Match(joined);
            if (heading.Success)
            {
                // The heading prints twice per account — once in the summary at
                // the top of the page and once over the table — so an account
                // already open is continued rather than started again.
                var type = heading.Groups[1].Value;
                current = accounts.Find(a => a.Type == type);

                if (current is null)
                {
                    current = new Account(type);
                    accounts.Add(current);
                }

                pending = Label.None;
                continue;
            }

            if (current is null) continue;

            // A label on its own line, whose figure is on the next one.
            if (joined.StartsWith("Current Balance", StringComparison.Ordinal)) { pending = Label.Current; continue; }
            if (joined.StartsWith("Beginning Balance", StringComparison.Ordinal)) { pending = Label.Beginning; continue; }

            if (pending != Label.None)
            {
                var figure = Dollars(line[SofiStatementGrid.ColDate]);
                if (figure is not null)
                {
                    if (pending == Label.Current) current.CurrentBalance ??= figure;
                    else current.BeginningBalance ??= figure;
                    pending = Label.None;
                    continue;
                }
            }

            // The id prints on its own line under the entry it belongs to.
            var id = TransactionIdPattern().Match(line[SofiStatementGrid.ColDescription].Trim());
            if (id.Success)
            {
                if (current.Rows.Count > 0 && current.Rows[^1].TransactionId.Length == 0)
                    current.Rows[^1] = current.Rows[^1] with { TransactionId = id.Groups[1].Value };

                continue;
            }

            var date = IsoDate(line[SofiStatementGrid.ColDate]);
            var amount = Dollars(line[SofiStatementGrid.ColAmount]);
            var balance = Dollars(line[SofiStatementGrid.ColBalance]);

            if (date is null || amount is null || balance is null) continue;

            var cents = Cents(amount);

            current.Rows.Add(new SofiRow
            {
                // Filled in by the "Transaction ID:" line that follows. A row
                // that never gets one is caught by the reconciliation, which
                // refuses an empty id outright.
                TransactionId = "",
                Date = date,
                Type = CollapseWhitespace(line[SofiStatementGrid.ColType]),
                Description = CollapseWhitespace(line[SofiStatementGrid.ColDescription]),
                Amount = Math.Abs(cents / 100m).ToString("0.00", CultureInfo.InvariantCulture),
                IsCredit = cents > 0,
                Balance = balance.Replace("$", "", StringComparison.Ordinal),
                AccountType = current.Type,
                StatementDate = statementDate,
            });
        }

        return accounts;
    }

    private enum Label
    {
        None,
        Current,
        Beginning,
    }

    // ── Does it add up? ──────────────────────────────────────────────────────

    /// The rows are printed newest first, so the chain is walked in that
    /// direction: the newest row's balance is the account's Current Balance, each
    /// row's balance less its own amount is the balance the row below it left,
    /// and the oldest row has to land on the Beginning Balance.
    ///
    /// That is stronger than summing to a total, and it is the reason to bother:
    /// a sum says only that the statement is short by some figure, where this
    /// says which row the chain broke at.
    private static string? Reconcile(Account account)
    {
        if (account.CurrentBalance is null || account.BeginningBalance is null)
        {
            return $"Couldn't find the {account.Type} account's own balances, so the parse can't be reconciled — nothing was imported.";
        }

        var missing = account.Rows.Find(r => r.TransactionId.Length == 0);
        if (missing is not null)
        {
            return $"The {account.Type} entry \"{missing.Description}\" on {missing.Date} has no transaction id printed under it, "
                + "so it could not be identified — nothing was imported.";
        }

        var running = Cents(account.CurrentBalance);

        foreach (var row in account.Rows)
        {
            if (Cents(row.Balance) != running)
            {
                return $"The {account.Type} entry \"{row.Description}\" on {row.Date} closes at {Usd(Cents(row.Balance))} "
                    + $"but the entry above it leaves {Usd(running)}. The PDF didn't parse cleanly — nothing was imported.";
            }

            running -= row.IsCredit ? Cents(row.Amount) : -Cents(row.Amount);
        }

        if (running != Cents(account.BeginningBalance))
        {
            return $"The {account.Type} rows work back to {Usd(running)} but the statement opens at {account.BeginningBalance}. "
                + "The PDF didn't parse cleanly — nothing was imported.";
        }

        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Joined(string[] line) =>
        string.Join(' ', line.Where(c => c.Length > 0));

    /// "Feb 28, 2026" as printed, to an ISO date. SoFi prints the year on every
    /// row, so nothing is inferred from the statement period.
    private static string? IsoDate(string cell)
    {
        var match = EntryDatePattern().Match(cell.Trim());
        if (!match.Success) return null;

        var month = Array.FindIndex(
            Months,
            m => match.Groups[1].Value.Equals(m, StringComparison.OrdinalIgnoreCase)) + 1;
        if (month == 0) return null;

        var day = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var year = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

        return $"{year:D4}-{month:D2}-{day:D2}";
    }

    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// A dollar figure as printed, sign kept. Only a properly formatted one
    /// counts: interest rates ("0.50%") and account numbers print in these
    /// columns too.
    private static string? Dollars(string cell)
    {
        var match = DollarsPattern().Match(cell.Trim());
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string Normalise(string value) =>
        CollapseWhitespace(value).Replace('’', '\'');

    private static string CollapseWhitespace(string value) =>
        WhitespaceRunPattern().Replace(value, " ").Trim();

    private static long Cents(string printed)
    {
        var cleaned = printed
            .Replace(",", "", StringComparison.Ordinal)
            .Replace("$", "", StringComparison.Ordinal);

        var value = decimal.Parse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture);
        return (long)decimal.Round(value * 100, 0, MidpointRounding.AwayFromZero);
    }

    /// Match the statement's own formatting so the two figures in an error
    /// message can be read side by side.
    private static string Usd(long cents) =>
        (cents / 100m).ToString("$#,##0.00;-$#,##0.00", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+(\d{1,2}),\s+(\d{4})$")]
    private static partial Regex EntryDatePattern();

    [GeneratedRegex(@"(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{1,2},\s+\d{4}\s*[-–]\s*(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{1,2},\s+(\d{4})")]
    private static partial Regex PeriodPattern();

    [GeneratedRegex(@"^(Checking|Savings) Account\s*-\s*\d+$")]
    private static partial Regex AccountHeadingPattern();

    [GeneratedRegex(@"^Transaction ID:\s+(\S+)$")]
    private static partial Regex TransactionIdPattern();

    [GeneratedRegex(@"^(-?\$[\d,]+\.\d{2})$")]
    private static partial Regex DollarsPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunPattern();

    [GeneratedRegex(@"\bSoFi\b", RegexOptions.IgnoreCase)]
    private static partial Regex SofiMarker();

    [GeneratedRegex("American Express|Membership Rewards", RegexOptions.IgnoreCase)]
    private static partial Regex AmericanExpressMarker();

    [GeneratedRegex(@"\bBarclay", RegexOptions.IgnoreCase)]
    private static partial Regex BarclaysMarker();

    [GeneratedRegex(@"\bHSBC\b", RegexOptions.IgnoreCase)]
    private static partial Regex HsbcMarker();

    [GeneratedRegex(@"\bSantander\b", RegexOptions.IgnoreCase)]
    private static partial Regex SantanderMarker();

    [GeneratedRegex(@"\bChase\b|JPMorgan", RegexOptions.IgnoreCase)]
    private static partial Regex ChaseMarker();
}
