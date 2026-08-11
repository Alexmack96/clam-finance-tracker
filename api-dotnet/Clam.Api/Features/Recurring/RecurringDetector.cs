using Clam.Api.Domain;

namespace Clam.Api.Features.Recurring;

public enum RecurringCadence
{
    Weekly,
    Fortnightly,
    Monthly,
    Quarterly,
    Yearly,
}

/// The minimum a transaction has to expose to be considered.
public sealed class RecurringCandidate
{
    public string Description { get; set; } = "";
    public DateTime Date { get; set; }

    /// Always positive; direction comes from <see cref="Type"/>.
    public decimal Amount { get; set; }

    public TransactionType Type { get; set; }

    /// Namespace from `externalId` (`monzo`, `amex`, …), or null.
    public string? Bank { get; set; }

    public Bucket? Bucket { get; set; }
    public string CategoryName { get; set; } = "";
}

public sealed class RecurringSeries
{
    /// Stable identity: exact description is *why* these rows grouped at all.
    public string Description { get; set; } = "";

    public TransactionType Kind { get; set; }
    public RecurringCadence Cadence { get; set; }

    /// Median days between occurrences.
    public int MedianGapDays { get; set; }

    public int Occurrences { get; set; }

    /// The worst gap's deviation from the median, as a fraction of it. Lower is
    /// tighter.
    public double Irregularity { get; set; }

    /// Seen ÷ predicted-by-cadence, over the window observed.
    public double Coverage { get; set; }

    public DateTime LastDate { get; set; }
    public double LastAmount { get; set; }

    /// Mean of every occurrence — the honest figure for a variable bill.
    public double AverageAmount { get; set; }

    /// Projected from <see cref="LastDate"/> + <see cref="MedianGapDays"/>.
    public DateTime NextDueDate { get; set; }

    public Bucket? Bucket { get; set; }
    public string CategoryName { get; set; } = "";
    public string? Bank { get; set; }

    /// Still running, judged against this bank's own data horizon.
    public bool Active { get; set; }

    public int DaysSinceLast { get; set; }

    /// Set when this series' bank has no data since this date — <see cref="Active"/>
    /// is then a statement about stale data, not about the subscription.
    public DateTime? BankDataEndsAt { get; set; }
}

/// Recurring-payment detection.
///
/// The load-bearing insight is that **timing, not amount, is the signal**. A
/// subscription and a fortnightly coffee habit are indistinguishable by interval
/// *length*, but not by interval *consistency* — real recurring items land
/// within ~10% of their cadence, habits scatter by 150–1200%. Amount stability
/// is deliberately unused: variable bills (energy, council tax) swing wildly in
/// price while keeping rigid timing, and gating on amount would drop them.
///
/// Feature-shared rather than app-wide: only the Recurring slices run this. If
/// a forecast feature ever needs it too, it moves to Domain/ and not before.
public static class RecurringDetector
{
    /// Named so a reader can see why a fortnightly coffee run does not qualify.
    private const int MinOccurrences = 3;

    /// Real items measure 3–18%; the nearest habit is 150%.
    private const double MaxIrregularity = 0.25;

    /// Real items measure 80–100%; short-cadence flukes measure 23–27%.
    private const double MinCoverage = 0.6;

    /// One missed cycle is a blip; half a cycle beyond that is a cancellation.
    private const double ActiveCycleTolerance = 1.5;

    private static readonly (RecurringCadence Cadence, double Min, double Max)[] Bands =
    [
        (RecurringCadence.Weekly, 5, 9),
        (RecurringCadence.Fortnightly, 12, 16),
        (RecurringCadence.Monthly, 26, 35),
        (RecurringCadence.Quarterly, 85, 100),
        (RecurringCadence.Yearly, 350, 380),
    ];

    /// Group by exact description and keep the groups that recur on a tight
    /// schedule.
    ///
    /// Exact matching is deliberate: normalising bank references away was
    /// measured against this dataset and recovered *zero* additional series. The
    /// noisy-reference descriptions are ad-hoc card payments, which fail the
    /// timing test whether or not they group.
    public static List<RecurringSeries> Detect(IReadOnlyList<RecurringCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var horizons = BankHorizons(candidates);

        // "Is it still running?" has to be asked against the data we actually
        // hold. A bank whose last statement was three months ago would otherwise
        // report every one of its subscriptions as cancelled.
        var globalHorizon = horizons.Count == 0 ? DateTime.MinValue : horizons.Values.Max();

        var series = new List<RecurringSeries>();

        foreach (var group in candidates.GroupBy(c => c.Description, StringComparer.Ordinal))
        {
            var sorted = group.OrderBy(c => c.Date).ToList();
            if (sorted.Count < MinOccurrences) continue;

            var gaps = new double[sorted.Count - 1];
            for (var i = 1; i < sorted.Count; i++)
                gaps[i - 1] = (sorted[i].Date - sorted[i - 1].Date).TotalDays;

            var medianGap = Median(gaps);

            // Several same-day charges give a median of 0, which would sail
            // through every test below.
            if (medianGap <= 0) continue;

            var band = Array.Find(Bands, b => medianGap >= b.Min && medianGap <= b.Max);
            if (band.Min == 0 && band.Max == 0) continue;

            var irregularity = gaps.Max(g => Math.Abs(g - medianGap)) / medianGap;
            if (irregularity > MaxIrregularity) continue;

            var last = sorted[^1];
            var horizon = last.Bank is not null && horizons.TryGetValue(last.Bank, out var bankHorizon)
                ? bankHorizon
                : globalHorizon;

            // How many occurrences the cadence predicts across the window we
            // observed.
            var windowDays = (horizon - sorted[0].Date).TotalDays;
            var predicted = Math.Max((int)Math.Floor(windowDays / medianGap) + 1, 1);
            var coverage = (double)sorted.Count / predicted;
            if (coverage < MinCoverage) continue;

            var daysSinceLast = (horizon - last.Date).TotalDays;
            var amounts = sorted.Select(r => (double)r.Amount).ToList();

            series.Add(new RecurringSeries
            {
                Description = group.Key,
                Kind = last.Type,
                Cadence = band.Cadence,
                MedianGapDays = (int)Math.Round(medianGap, MidpointRounding.AwayFromZero),
                Occurrences = sorted.Count,
                Irregularity = irregularity,
                Coverage = Math.Min(coverage, 1),
                LastDate = last.Date,
                LastAmount = amounts[^1],
                AverageAmount = amounts.Average(),
                NextDueDate = last.Date.AddDays(medianGap),
                Bucket = last.Bucket,
                CategoryName = last.CategoryName,
                Bank = last.Bank,
                Active = daysSinceLast <= medianGap * ActiveCycleTolerance,
                DaysSinceLast = (int)Math.Round(daysSinceLast, MidpointRounding.AwayFromZero),

                // Only worth surfacing when this bank lags the freshest data we
                // hold — otherwise every series on the newest bank carries a
                // pointless badge.
                BankDataEndsAt = last.Bank is not null
                    && horizons.TryGetValue(last.Bank, out var ownHorizon)
                    && ownHorizon < globalHorizon
                        ? ownHorizon
                        : null,
            });
        }

        return [.. series.OrderByDescending(s => s.LastAmount)];
    }

    /// What a cadence costs per month, so weekly and yearly items can be
    /// totalled against each other. Uses the average rather than the last
    /// amount — a variable bill's most recent charge is not its typical one.
    public static double MonthlyEquivalent(RecurringSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return series.AverageAmount * 365.25 / (series.MedianGapDays * 12);
    }

    /// Latest transaction date per bank.
    private static Dictionary<string, DateTime> BankHorizons(IReadOnlyList<RecurringCandidate> candidates)
    {
        var horizons = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var c in candidates)
        {
            if (c.Bank is null) continue;
            if (!horizons.TryGetValue(c.Bank, out var current) || c.Date > current)
                horizons[c.Bank] = c.Date;
        }
        return horizons;
    }

    /// The upper median, matching the JavaScript `sorted[floor(len / 2)]` this
    /// was ported from. A different tie-break would move series across the
    /// cadence band boundaries.
    private static double Median(double[] values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }
}
