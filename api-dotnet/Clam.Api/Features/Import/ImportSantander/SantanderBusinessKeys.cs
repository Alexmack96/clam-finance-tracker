using System.Security.Cryptography;
using System.Text;

namespace Clam.Api.Features.Import.ImportSantander;

/// Deterministic content-hash id per row, so re-uploading a statement produces
/// the same ids and its rows are recognised as duplicates rather than imported
/// twice. Identical rows within one statement are kept distinct by a suffix —
/// and this account genuinely prints those: two £1,000.00 transfers to the same
/// payee on the same day, one after the other.
///
/// The owner is not part of the key, matching Barclays and HSBC and unlike Amex:
/// this is one account, so the same row cannot legitimately belong to two
/// people.
///
/// The running balance *is* part of the key, unlike HSBC's, and that is the
/// difference worth knowing. Santander prints a balance on every row, so it
/// costs nothing to include and it is what separates the two identical
/// transfers above by more than a counter. It does mean a statement that
/// overlapped another would re-key its shared rows — Santander's statements
/// abut rather than overlap, opening the day after the last one closed, so
/// there is no overlap for it to spoil. The Express importer keyed the same
/// five fields, so ids carried across match.
public static class SantanderBusinessKeys
{
    private const int IdLength = 16;

    public static IReadOnlyList<KeyedSantanderRow> Assign(IReadOnlyList<SantanderRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var keyed = new List<KeyedSantanderRow>(rows.Count);

        // Statement order, which is the order the entries were printed in and is
        // already stable across re-parses of the same file.
        foreach (var row in rows)
        {
            var baseId = Hash(ContentKey(row));
            var seen = counts.GetValueOrDefault(baseId);
            counts[baseId] = seen + 1;
            keyed.Add(new KeyedSantanderRow(row, seen == 0 ? baseId : $"{baseId}-{seen}"));
        }

        return keyed;
    }

    private static string ContentKey(SantanderRow row) =>
        $"{row.Date}|{row.Description}|{row.MoneyIn ?? ""}|{row.MoneyOut ?? ""}|{row.Balance}";

    private static string Hash(string contentKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(contentKey)))[..IdLength];
}

public sealed record KeyedSantanderRow(SantanderRow Row, string TransactionId);
