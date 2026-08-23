using System.Security.Cryptography;
using System.Text;

namespace Clam.Api.Features.Import.ImportChase;

/// Deterministic content-hash id per row, so re-uploading a statement produces
/// the same ids and its rows are recognised as duplicates rather than imported
/// twice. Identical rows within one statement are kept distinct by a suffix —
/// and this card genuinely prints those: two identical Stagecoach fares on the
/// same day, one after the other.
///
/// The owner is not part of the key, matching the other single-holder cards.
/// The same four fields the Express importer hashed, so ids carried across
/// match.
public static class ChaseBusinessKeys
{
    private const int IdLength = 16;

    public static IReadOnlyList<KeyedChaseRow> Assign(IReadOnlyList<ChaseRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var keyed = new List<KeyedChaseRow>(rows.Count);

        foreach (var row in rows)
        {
            var baseId = Hash(ContentKey(row));
            var seen = counts.GetValueOrDefault(baseId);
            counts[baseId] = seen + 1;
            keyed.Add(new KeyedChaseRow(row, seen == 0 ? baseId : $"{baseId}-{seen}"));
        }

        return keyed;
    }

    private static string ContentKey(ChaseRow row) =>
        $"{row.Date}|{row.Description}|{row.Amount}|{row.IsCredit}";

    private static string Hash(string contentKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(contentKey)))[..IdLength];
}

public sealed record KeyedChaseRow(ChaseRow Row, string TransactionId);
