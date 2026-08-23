namespace Clam.Api.Features.Import.ImportSofi;

/// SoFi's own transaction ids, kept rather than replaced with a content hash.
///
/// This is the one bank that prints a real per-row identifier, and they are
/// unique across both accounts in a statement and across statements — the
/// Checking account numbers in the 400s and the Savings account in the 100s,
/// both climbing month on month. An id the bank assigned survives a statement
/// being reissued with a corrected description, where a content hash does not.
///
/// The in-batch counter is kept all the same. It costs nothing and it is the
/// difference between a repeated id being noticed and one row quietly replacing
/// another. The Express importer keyed the same way, so ids carried across from
/// it match.
public static class SofiBusinessKeys
{
    public static IReadOnlyList<KeyedSofiRow> Assign(IReadOnlyList<SofiRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var keyed = new List<KeyedSofiRow>(rows.Count);

        foreach (var row in rows)
        {
            var baseId = row.TransactionId;
            var seen = counts.GetValueOrDefault(baseId);
            counts[baseId] = seen + 1;
            keyed.Add(new KeyedSofiRow(row, seen == 0 ? baseId : $"{baseId}-{seen}"));
        }

        return keyed;
    }
}

public sealed record KeyedSofiRow(SofiRow Row, string TransactionId);
