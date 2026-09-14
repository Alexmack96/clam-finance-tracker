namespace Clam.Api.Features.Monzo.GetFlexRec;

public enum FlexRepaymentStatus
{
    /// The balance was within tolerance of zero straight after it.
    Cleared,

    /// It left a balance, and a later repayment cleared the card.
    ClearedLater,

    /// It left a balance, and no repayment since has cleared the card.
    Outstanding,
}

public sealed class FlexRepayment
{
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public FlexRepaymentStatus Status { get; set; }
}

public sealed class GetFlexRecResponse
{
    /// What is owed to Flex now. Negative when Flex owes you, e.g. a refund on a
    /// statement that was already paid.
    public decimal Balance { get; set; }

    public decimal Purchases { get; set; }
    public decimal Repaid { get; set; }

    /// Credits that are not repayments: merchant refunds.
    public decimal Refunds { get; set; }

    /// Repayments that left a balance no later repayment cleared.
    public int Outstanding { get; set; }

    /// Oldest first.
    public IReadOnlyList<FlexRepayment> Repayments { get; set; } = [];
}
