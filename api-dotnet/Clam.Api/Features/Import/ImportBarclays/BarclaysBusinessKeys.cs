using System.Security.Cryptography;
using System.Text;

namespace Clam.Api.Features.Import.ImportBarclays;

/// Deterministic content-hash id per row, so re-uploading a statement produces
/// the same ids and its rows are recognised as duplicates rather than imported
/// twice. Identical rows within one statement are kept distinct by a suffix —
/// and Barclaycard genuinely prints those: two £7.50 golf bookings on the same
/// day, one after the other.
///
/// The owner is not part of the key, matching HSBC and unlike Amex: this is one
/// card with one holder, so the same row cannot legitimately belong to two
/// people.
///
/// These ids differ from the Express importer's, which hashed the same four
/// fields but over a description built by a parser that dropped the currency and
/// rate lines under a foreign charge. Same rule, different input, so the same
/// charge keys differently in the two databases. That is the accepted cost of
/// re-uploading the statements rather than migrating them.
public static class BarclaysBusinessKeys
{
    private const int IdLength = 16;

    public static IReadOnlyList<KeyedBarclaysRow> Assign(IReadOnlyList<BarclaysRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var keyed = new List<KeyedBarclaysRow>(rows.Count);

        // Statement order, which is the order the entries were printed in and is
        // already stable across re-parses of the same file.
        foreach (var row in rows)
        {
            var baseId = Hash(ContentKey(row));
            var seen = counts.GetValueOrDefault(baseId);
            counts[baseId] = seen + 1;
            keyed.Add(new KeyedBarclaysRow(row, seen == 0 ? baseId : $"{baseId}-{seen}"));
        }

        return keyed;
    }

    private static string ContentKey(BarclaysRow row) =>
        $"{row.Date}|{row.Description}|{row.Amount}|{row.IsCredit}";

    private static string Hash(string contentKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(contentKey)))[..IdLength];
}

public sealed record KeyedBarclaysRow(BarclaysRow Row, string TransactionId);
