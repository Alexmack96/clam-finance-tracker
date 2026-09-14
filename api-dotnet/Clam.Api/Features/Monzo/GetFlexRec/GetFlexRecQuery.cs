using Clam.Api.Domain;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Monzo.GetFlexRec;

/// Rebuilds the Flex card's balance from its own transactions. Purchases add to
/// it, every credit takes it off, and each repayment should bring it back to
/// zero: if one did not, and no later repayment did either, a purchase or a
/// payment is missing, or a statement was not paid in full.
public sealed class GetFlexRecQuery(IDbConnectionFactory factory)
{
    /// Within this much of zero a repayment counts as having cleared the card. A
    /// refund landing after a statement was paid leaves pennies over, which is
    /// not a missing transaction.
    internal const decimal ClearedTolerance = 1.00m;

    /// Monzo files a Flex repayment under `transfers`. Rows with no staging row
    /// to ask fall back to the name Monzo gives the repayment.
    private const string RepaymentCategory = "transfers";
    private const string RepaymentName = "Flex";

    private const string Sql = """
        SELECT  t.[date], t.[description], t.[type], t.[amount], m.[monzoCategory]
        FROM    [Transactions] t
        LEFT JOIN [MonzoApiTransactions] m ON t.[externalId] = CONCAT('flex:', m.[monzoId])
        WHERE   t.[externalId] LIKE 'flex:%'
        ORDER BY t.[date], t.[id];
        """;

    public async Task<GetFlexRecResponse> ExecuteAsync(CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<FlexRow>(new CommandDefinition(Sql, cancellationToken: ct));
        return Reconcile([.. rows]);
    }

    private static GetFlexRecResponse Reconcile(IReadOnlyList<FlexRow> rows)
    {
        var balance = 0m;
        var purchases = 0m;
        var refunds = 0m;
        var repayments = new List<FlexRepayment>();

        foreach (var row in rows)
        {
            if (row.Type == TransactionType.Expense)
            {
                balance += row.Amount;
                purchases += row.Amount;
                continue;
            }

            var before = balance;
            balance -= row.Amount;

            if (!IsRepayment(row))
            {
                refunds += row.Amount;
                continue;
            }

            repayments.Add(new FlexRepayment
            {
                Date = row.Date,
                Amount = row.Amount,
                BalanceBefore = before,
                BalanceAfter = balance,
                Status = Math.Abs(balance) <= ClearedTolerance
                    ? FlexRepaymentStatus.Cleared
                    : FlexRepaymentStatus.Outstanding,
            });
        }

        // A repayment that left a balance is only a problem if nothing after it
        // cleared the card. Purchases made after a statement closed legitimately
        // roll into the next payment.
        var clearedSince = false;
        for (var i = repayments.Count - 1; i >= 0; i--)
        {
            if (repayments[i].Status == FlexRepaymentStatus.Cleared) clearedSince = true;
            else if (clearedSince) repayments[i].Status = FlexRepaymentStatus.ClearedLater;
        }

        return new GetFlexRecResponse
        {
            Balance = balance,
            Purchases = purchases,
            Repaid = repayments.Sum(r => r.Amount),
            Refunds = refunds,
            Outstanding = repayments.Count(r => r.Status == FlexRepaymentStatus.Outstanding),
            Repayments = repayments,
        };
    }

    private static bool IsRepayment(FlexRow row) =>
        row.MonzoCategory is not null
            ? string.Equals(row.MonzoCategory, RepaymentCategory, StringComparison.Ordinal)
            : string.Equals(row.Description, RepaymentName, StringComparison.OrdinalIgnoreCase);

    private sealed class FlexRow
    {
        public DateTime Date { get; set; }
        public string Description { get; set; } = "";
        public TransactionType Type { get; set; }
        public decimal Amount { get; set; }
        public string? MonzoCategory { get; set; }
    }
}
