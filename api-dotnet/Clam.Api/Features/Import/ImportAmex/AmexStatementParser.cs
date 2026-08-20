using System.Globalization;
using System.Text.RegularExpressions;

namespace Clam.Api.Features.Import.ImportAmex;

/// Reads an Amex statement PDF into rows, and refuses to hand back rows it
/// cannot prove correct.
///
/// The proof matters more than the parse. A statement is the only record of what
/// was actually charged, so a silently mis-read row becomes a wrong number that
/// nothing downstream can detect. Every parse is therefore checked against the
/// statement's own arithmetic before it is allowed out of here, and a failure
/// rejects the whole upload rather than importing part of it.
public static partial class AmexStatementParser
{
    /// Amex prints each rate to 4dp and truncates rather than rounds, so the
    /// reconstructed sterling drifts from the printed figure in proportion to
    /// the amount — about 9p on £1,246. The tolerance has to scale with the row
    /// or it flags large legitimate charges; the flat penny absorbs the final
    /// rounding of the division itself.
    private const decimal RateTruncationTolerance = 0.0001m;
    private const decimal FlatRoundingTolerance = 0.01m;

    public static AmexParseResult Parse(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        List<string[]> grid;
        try
        {
            grid = AmexStatementGrid.Build(pdf);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return AmexParseResult.Rejected(400, "Failed to extract text from PDF");
        }

        var guard = CheckIsAmexStatement(grid);
        if (guard is not null) return AmexParseResult.Rejected(422, guard);

        var statementDate = FindStatementDate(grid);
        if (statementDate is null)
            return AmexParseResult.Rejected(400, "Could not find statement date in PDF");

        var rows = ReadRows(grid, statementDate, out var rateChecks, out var rowError);
        if (rowError is not null) return AmexParseResult.Rejected(400, rowError);

        var mismatch = Reconcile(rows, grid) ?? CheckPrintedRates(rateChecks);
        if (mismatch is not null) return AmexParseResult.Rejected(422, mismatch);

        return AmexParseResult.Parsed(rows, statementDate);
    }

    // ── Is this even an Amex statement? ──────────────────────────────────────
    //
    // Every bank endpoint accepts any PDF, so it is easy to upload one bank's
    // statement under another's card. A statement's text always carries the
    // issuing bank's name in the header or the legal footer, so requiring that
    // marker turns a silent bad import into a clear 422. The positive check is
    // the decider; recognising another bank only makes the message friendlier.
    //
    // Slice-local while Amex is the only bank ported. The third bank to need
    // this moves it to Features/Import/, not before.
    /// The article is stored rather than derived. A first-letter vowel test gets
    /// "an HSBC statement" wrong, because the rule is about how the name is said,
    /// not how it is spelled.
    private static readonly (string Article, string Label, Regex Pattern)[] BankMarkers =
    [
        ("an", "American Express", AmericanExpressMarker()),
        ("a", "Barclays", BarclaysMarker()),
        ("a", "Santander", SantanderMarker()),
        ("an", "HSBC", HsbcMarker()),
        ("a", "Chase", ChaseMarker()),
        ("a", "SoFi", SofiMarker()),
    ];

    private static string? CheckIsAmexStatement(List<string[]> grid)
    {
        var text = string.Join('\n', grid.Select(line => string.Join('\t', line)));

        if (BankMarkers[0].Pattern.IsMatch(text)) return null;

        var other = BankMarkers.Skip(1).FirstOrDefault(b => b.Pattern.IsMatch(text));
        return other.Label is not null
            ? $"This looks like {other.Article} {other.Label} statement, not American Express. Upload it under the {other.Label} card instead."
            : "This doesn't look like an American Express statement — no \"American Express\" marker found. Did you upload the right bank's file?";
    }

    // ── Rows ─────────────────────────────────────────────────────────────────

    private static string? FindStatementDate(List<string[]> grid)
    {
        // Every page header carries it in the amount column.
        foreach (var line in grid)
        {
            var match = StatementDatePattern().Match(line[AmexStatementGrid.ColAmount]);
            if (match.Success) return match.Groups[1].Value;
        }

        return null;
    }

    private static List<AmexRow> ReadRows(
        List<string[]> grid,
        string statementDate,
        out List<PrintedRate> rateChecks,
        out string? error)
    {
        rateChecks = [];
        error = null;

        var statementMonth = int.Parse(statementDate.Split('/')[1], CultureInfo.InvariantCulture);
        var statementYear = 2000 + int.Parse(statementDate.Split('/')[2], CultureInfo.InvariantCulture);

        var rows = new List<AmexRow>();

        // The row a trailing "CR", currency name or exchange-rate line belongs
        // to. Cleared at section totals and page headers so a marker can never
        // latch across a boundary — the "CR" under "Total of other account
        // transactions" qualifies the total, not the last credit above it.
        AmexRow? last = null;

        foreach (var line in grid)
        {
            var amountCell = line[AmexStatementGrid.ColAmount];
            var description = line[AmexStatementGrid.ColDescription];

            // Amex prints the rate it actually used under each foreign row. It
            // is not a transaction, but it is the per-row checksum, so it is
            // collected before the row is dismissed as a continuation line.
            var rate = ExchangeRatePattern().Match(description);
            if (rate.Success && last?.ForeignAmount is not null)
            {
                rateChecks.Add(new PrintedRate(
                    last,
                    decimal.Parse(rate.Groups[1].Value, CultureInfo.InvariantCulture),
                    decimal.Parse(rate.Groups[2].Value, CultureInfo.InvariantCulture)));
            }

            // The statement carries on past "Total new spend transactions" into
            // OTHER ACCOUNT TRANSACTIONS — statement credits (a Deliveroo Gold
            // benefit, say) that are not card spend but do count towards New
            // Credits, so they are imported too.
            if (line[AmexStatementGrid.ColTransactionDate].StartsWith("Total ", StringComparison.OrdinalIgnoreCase)
                || StatementDatePattern().IsMatch(amountCell))
            {
                last = null;
                continue;
            }

            // "CR" is printed on its own line just below the amount it qualifies.
            if (amountCell == "CR")
            {
                if (last is not null) last.IsCredit = true;
                continue;
            }

            var transaction = MonthDayPattern().Match(line[AmexStatementGrid.ColTransactionDate]);
            var processed = MonthDayPattern().Match(line[AmexStatementGrid.ColProcessDate]);
            if (!transaction.Success || !processed.Success)
            {
                // Continuation line: a foreign row prints its currency name
                // underneath the foreign amount.
                if (last?.ForeignAmount is not null && last.ForeignCurrency is null)
                {
                    var currency = line[AmexStatementGrid.ColForeign];
                    if (CurrencyNamePattern().IsMatch(currency)) last.ForeignCurrency = currency;
                }

                continue;
            }

            if (!MoneyPattern().IsMatch(amountCell))
            {
                error = $"Amex row \"{line[AmexStatementGrid.ColTransactionDate]} {description}\" has no amount in the Amount £ column";
                return rows;
            }

            var foreignAmount = line[AmexStatementGrid.ColForeign];
            last = new AmexRow
            {
                TransactionDate = IsoDate(transaction, statementMonth, statementYear),
                ProcessDate = IsoDate(processed, statementMonth, statementYear),
                Description = CollapseWhitespace(description),
                Amount = amountCell,
                IsCredit = PaymentReceivedPattern().IsMatch(description),
                ForeignAmount = MoneyPattern().IsMatch(foreignAmount) ? foreignAmount : null,
                StatementDate = statementDate,
            };
            rows.Add(last);
        }

        return rows;
    }

    /// Amex pads descriptions so the merchant and the city line up in the
    /// printed column — "LIME*RIDE KHJA          LONDON". That padding is
    /// typography, not data, and it feeds the business key, so it is collapsed
    /// here rather than left for each consumer to remember.
    ///
    /// This is a deliberate divergence from the TypeScript parser, which kept
    /// the runs verbatim, and it is why the ported ids differ from the ones in
    /// production. See the re-key note on <see cref="AmexBusinessKeys"/>.
    private static string CollapseWhitespace(string value) =>
        WhitespaceRunPattern().Replace(value, " ").Trim();

    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static string IsoDate(Match monthDay, int statementMonth, int statementYear)
    {
        var month = Array.IndexOf(Months, monthDay.Groups[1].Value) + 1;
        var day = int.Parse(monthDay.Groups[2].Value, CultureInfo.InvariantCulture);

        // A statement runs from the 25th of one month to the 24th of the next,
        // so a row whose month is *after* the statement's belongs to the year
        // before it — December rows on a January statement.
        var year = month > statementMonth ? statementYear - 1 : statementYear;
        return $"{year:D4}-{month:D2}-{day:D2}";
    }

    // ── Reconciliation ───────────────────────────────────────────────────────

    /// Page 1's Account Summary box:
    ///
    ///     Previous Closing Balance − New Credits + New Debits = Closing Balance
    ///
    /// Those four figures bound the whole statement, so they are what the parsed
    /// rows are checked against. Σ debits must equal New Debits and Σ credits
    /// must equal New Credits, so a dropped, duplicated or mis-read row cannot
    /// slip through.
    ///
    /// The summary's own arithmetic is verified first: if that does not hold we
    /// read the wrong boxes, and the totals it yields mean nothing.
    private static string? Reconcile(List<AmexRow> rows, List<string[]> grid)
    {
        var summary = ReadAccountSummary(grid);
        if (summary is null)
            return "Couldn't find the Account Summary on page 1, so the parse can't be reconciled — nothing was imported.";

        var (previous, credits, debits, closing) = summary.Value;
        if (Pence(previous) - Pence(credits) + Pence(debits) != Pence(closing))
            return $"The Account Summary didn't add up (£{previous} − £{credits} + £{debits} ≠ £{closing}), so it was misread — nothing was imported.";

        foreach (var (label, matching, stated) in new[]
                 {
                     ("debits", rows.Where(r => !r.IsCredit), debits),
                     ("credits", rows.Where(r => r.IsCredit), credits),
                 })
        {
            var sum = matching.Sum(r => Pence(r.Amount));
            if (sum != Pence(stated))
                return $"Parsed {label} total {Gbp(sum)} but the statement's Account Summary says £{stated}. The PDF didn't parse cleanly — nothing was imported.";
        }

        return null;
    }

    /// The box straddles the transaction table's columns, so it is read off the
    /// whole line rather than by cell: the four £-prefixed amounts appear
    /// left-to-right in exactly that order.
    private static (string Previous, string Credits, string Debits, string Closing)? ReadAccountSummary(List<string[]> grid)
    {
        var lines = grid.Select(line => string.Join('\t', line)).ToList();
        var header = lines.FindIndex(l =>
            l.Contains("Previous Closing Balance", StringComparison.Ordinal)
            && l.Contains("New Credits", StringComparison.Ordinal));
        if (header < 0) return null;

        // The values print on the next row down; allow a little slack for stray
        // lines between the header and them.
        foreach (var line in lines.Skip(header + 1).Take(3))
        {
            var amounts = SummaryAmountPattern().Matches(line).Select(m => m.Groups[1].Value).ToList();
            if (amounts.Count < 4) continue;
            return (amounts[0], amounts[1], amounts[2], amounts[3]);
        }

        return null;
    }

    /// Per-row check, which the Account Summary cannot give you: on a foreign
    /// row Amex prints the rate and fee it applied, and
    ///
    ///     foreign amount / rate + fee = the sterling charged
    ///
    /// So a foreign amount read out of the wrong column fails here even when the
    /// statement's totals still add up — which is exactly the failure mode the
    /// positional grid exists to prevent, now checked rather than assumed.
    private static string? CheckPrintedRates(List<PrintedRate> rateChecks)
    {
        foreach (var (row, rate, fee) in rateChecks)
        {
            if (rate <= 0) continue;

            var converted = decimal.Round(Amount(row.ForeignAmount!) / rate, 2, MidpointRounding.AwayFromZero);
            var stated = Amount(row.Amount);
            var tolerance = (converted * RateTruncationTolerance) + FlatRoundingTolerance;

            if (Math.Abs(converted + fee - stated) > tolerance)
            {
                return $"\"{row.Description}\" doesn't match the rate printed under it "
                    + $"({row.ForeignAmount} {row.ForeignCurrency ?? "?"} ÷ {rate} + £{fee} ≠ £{row.Amount}). "
                    + "The PDF didn't parse cleanly — nothing was imported.";
            }
        }

        return null;
    }

    private static decimal Amount(string printed) =>
        decimal.Parse(printed.Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);

    private static long Pence(string printed) => (long)decimal.Round(Amount(printed) * 100, 0, MidpointRounding.AwayFromZero);

    /// Match the statement's own formatting so the two figures in an error
    /// message can be read side by side.
    private static string Gbp(long pence) =>
        (pence / 100m).ToString("£#,##0.00", CultureInfo.GetCultureInfo("en-GB"));

    private sealed record PrintedRate(AmexRow Row, decimal Rate, decimal Fee);

    [GeneratedRegex(@"^(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s*(\d{1,2})$")]
    private static partial Regex MonthDayPattern();

    [GeneratedRegex(@"^[\d,]+\.\d{2}$")]
    private static partial Regex MoneyPattern();

    [GeneratedRegex(@"^(\d{2}/\d{2}/\d{2})$")]
    private static partial Regex StatementDatePattern();

    [GeneratedRegex("^[A-Z][A-Z ]+$")]
    private static partial Regex CurrencyNamePattern();

    [GeneratedRegex(@"£\s*([\d,]+\.\d{2})")]
    private static partial Regex SummaryAmountPattern();

    [GeneratedRegex(@"^Exchange Rate ([\d.]+) \+ Nonsterling Transaction Fee ([\d.]+)$")]
    private static partial Regex ExchangeRatePattern();

    [GeneratedRegex("PAYMENT RECEIVED", RegexOptions.IgnoreCase)]
    private static partial Regex PaymentReceivedPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunPattern();

    [GeneratedRegex("American Express|Membership Rewards", RegexOptions.IgnoreCase)]
    private static partial Regex AmericanExpressMarker();

    // Barclays / Barclaycard.
    [GeneratedRegex(@"\bBarclay", RegexOptions.IgnoreCase)]
    private static partial Regex BarclaysMarker();

    [GeneratedRegex(@"\bSantander\b", RegexOptions.IgnoreCase)]
    private static partial Regex SantanderMarker();

    [GeneratedRegex(@"\bHSBC\b", RegexOptions.IgnoreCase)]
    private static partial Regex HsbcMarker();

    // \b avoids matching "purchase".
    [GeneratedRegex(@"\bChase\b|JPMorgan", RegexOptions.IgnoreCase)]
    private static partial Regex ChaseMarker();

    [GeneratedRegex(@"\bSoFi\b", RegexOptions.IgnoreCase)]
    private static partial Regex SofiMarker();
}
