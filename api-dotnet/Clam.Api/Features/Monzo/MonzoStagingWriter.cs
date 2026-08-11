using System.Data;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Monzo;
using Dapper;

namespace Clam.Api.Features.Monzo;

/// Writing API transactions into staging. Feature-shared (tier 2) so the
/// incremental sync and the reconciliation backfill stage byte-identical rows —
/// a row that arrives by the safety net must be indistinguishable from one the
/// sync caught, or the process step would treat them differently.
internal static class MonzoStagingWriter
{
    /// Insert-if-absent rather than a read followed by a write: the rec and a
    /// concurrent sync can reach the same transaction, and this is idempotent
    /// under both.
    private const string Sql = """
        INSERT INTO [MonzoApiTransactions]
            ([id], [monzoId], [created], [settled], [amountPence], [currency],
             [localAmountPence], [localCurrency], [description], [notes], [monzoCategory],
             [merchantName], [merchantEmoji], [merchantAddress], [scheme], [includeInSpending], [accountId])
        SELECT @Id, @MonzoId, @Created, @Settled, @AmountPence, @Currency,
               @LocalAmountPence, @LocalCurrency, @Description, @Notes, @MonzoCategory,
               @MerchantName, @MerchantEmoji, @MerchantAddress, @Scheme, @IncludeInSpending, @AccountId
        WHERE NOT EXISTS (SELECT 1 FROM [MonzoApiTransactions] WHERE [monzoId] = @MonzoId);
        """;

    internal static Task<int> StageAsync(
        IDbConnection connection,
        IIdGenerator ids,
        IReadOnlyList<MonzoTransaction> transactions,
        string accountId,
        CancellationToken ct)
    {
        var rows = transactions.Select(tx => new
        {
            Id = ids.NewId(),
            MonzoId = tx.Id,
            tx.Created,
            Settled = string.IsNullOrEmpty(tx.Settled) ? (DateTime?)null : DateTime.Parse(tx.Settled, null),
            AmountPence = tx.Amount,
            tx.Currency,
            LocalAmountPence = tx.LocalAmount,
            tx.LocalCurrency,
            tx.Description,
            Notes = string.IsNullOrEmpty(tx.Notes) ? null : tx.Notes,
            MonzoCategory = tx.Category,
            MerchantName = tx.Merchant?.Name,
            MerchantEmoji = tx.Merchant?.Emoji,
            MerchantAddress = tx.Merchant?.Address?.ShortFormatted,
            tx.Scheme,
            tx.IncludeInSpending,
            AccountId = accountId,
        }).ToList();

        return rows.Count == 0
            ? Task.FromResult(0)
            : connection.ExecuteAsync(new CommandDefinition(Sql, rows, cancellationToken: ct));
    }
}
