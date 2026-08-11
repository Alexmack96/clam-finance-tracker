using Clam.Api.Domain;

namespace Clam.Api.Features.Recurring.GetRecurring;

/// A detected series plus the human decision about it. Flat rather than a
/// series nested under a verdict: the client renders one row per series and the
/// verdict is three more cells on that row.
public sealed class RecurringSeriesView
{
    public string Description { get; set; } = "";
    public TransactionType Kind { get; set; }
    public RecurringCadence Cadence { get; set; }
    public int MedianGapDays { get; set; }
    public int Occurrences { get; set; }
    public double Irregularity { get; set; }
    public double Coverage { get; set; }
    public DateTime LastDate { get; set; }
    public double LastAmount { get; set; }
    public double AverageAmount { get; set; }
    public DateTime NextDueDate { get; set; }
    public Bucket? Bucket { get; set; }
    public string CategoryName { get; set; } = "";
    public string? Bank { get; set; }
    public bool Active { get; set; }
    public int DaysSinceLast { get; set; }
    public DateTime? BankDataEndsAt { get; set; }

    /// null means Proposed — the absence of a decision is the undecided state,
    /// so there is no third enum member to keep in sync.
    public RecurringStatus? Status { get; set; }

    public string? Note { get; set; }
    public double MonthlyEquivalent { get; set; }
}

public sealed class GetRecurringResponse
{
    public Owner Owner { get; set; }

    /// Income and expense are detected independently: a merchant that both
    /// charges and refunds on a schedule is two different facts about your money.
    public IReadOnlyList<RecurringSeriesView> Income { get; set; } = [];

    public IReadOnlyList<RecurringSeriesView> Expense { get; set; } = [];

    public double CommittedOutPerMonth { get; set; }
    public double CommittedInPerMonth { get; set; }
}
