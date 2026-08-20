using System.Security.Cryptography;
using System.Text;

namespace Clam.Api.Features.Import.ImportHsbc;

/// Deterministic content-hash id per row, so re-uploading a statement produces
/// the same ids and its rows are recognised as duplicates rather than imported
/// twice. Identical rows within one statement are kept distinct by a suffix.
///
/// Unlike Amex, the owner is not part of the key: HSBC statements are per
/// account, not per card, so the same row cannot legitimately belong to two
/// people. The balance is not part of it either — it is a running total, so
/// including it would make an id depend on every row printed before it, and
/// re-uploading a statement that overlaps another would produce fresh ids for
/// rows already imported.
public static class HsbcBusinessKeys
{
    private const int IdLength = 16;

    public static IReadOnlyList<KeyedHsbcRow> Assign(IReadOnlyList<HsbcRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var keyed = new List<KeyedHsbcRow>(rows.Count);

        // Statement order, not sorted order. HSBC rows carry a running balance,
        // so the printed order is the statement's own and is already stable
        // across re-parses of the same file.
        foreach (var row in rows)
        {
            var baseId = Hash(ContentKey(row));
            var seen = counts.GetValueOrDefault(baseId);
            counts[baseId] = seen + 1;
            keyed.Add(new KeyedHsbcRow(row, seen == 0 ? baseId : $"{baseId}-{seen}"));
        }

        return keyed;
    }

    private static string ContentKey(HsbcRow row) =>
        $"{row.Date}|{row.PaymentType}|{row.Description}|{row.MoneyIn ?? row.MoneyOut ?? ""}";

    private static string Hash(string contentKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(contentKey)))[..IdLength];
}

public sealed record KeyedHsbcRow(HsbcRow Row, string TransactionId);
