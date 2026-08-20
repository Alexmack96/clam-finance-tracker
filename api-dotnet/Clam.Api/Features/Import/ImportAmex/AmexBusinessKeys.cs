using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Clam.Api.Features.Import.ImportAmex;

/// Gives every parsed row a deterministic id derived from its content, so that
/// re-uploading a statement produces the *same* ids and its rows are recognised
/// as duplicates instead of imported twice.
///
/// The hard case is that two genuinely distinct charges can be identical on
/// every field a statement prints — 10 July has two LIME*RIDE KHJA £1.70 hires,
/// and both are real. So identical content keys are kept distinct by a suffix,
/// and rows are sorted by content key *before* suffixes are assigned, so the ids
/// do not depend on the order the parser happened to produce them in.
///
/// These ids differ from the ones the TypeScript importer produced, because
/// descriptions now have their column padding collapsed
/// (<c>LIME*RIDE KHJA          LONDON</c> → <c>LIME*RIDE KHJA LONDON</c>). That
/// padding is typography, not data: keying on it makes an id depend on how many
/// spaces Amex happened to use to line a column up. Every other field is
/// byte-identical — verified row by row against the TypeScript parser over four
/// statements.
///
/// The old rows are not migrated. The statements are re-uploaded into this
/// database instead, which is both cheaper and a chance to retire the two id
/// schemes already living side by side in the Express data. So: a row that
/// arrives here by any route *other* than a fresh upload keeps its old id, and
/// will not be recognised as a duplicate of the same charge imported properly.
public static class AmexBusinessKeys
{
    /// Enough of SHA-256 to make a collision between two *different* content
    /// keys implausible, while staying short enough to read in a log line.
    private const int IdLength = 16;

    public static IReadOnlyList<KeyedAmexRow> Assign(IReadOnlyList<AmexRow> rows, string owner)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        // Ordinal, not a culture-aware comparison: this ordering decides which
        // of a set of identical rows gets the suffix, so it has to mean the same
        // thing on every machine. It cannot change *which* ids are produced —
        // only rows with equal keys are affected, and those are by definition
        // indistinguishable — but a stable order keeps the output reproducible.
        return [.. rows
            .OrderBy(r => ContentKey(r, owner), StringComparer.Ordinal)
            .Select(row =>
            {
                var baseId = Hash(ContentKey(row, owner));
                var seen = counts.GetValueOrDefault(baseId);
                counts[baseId] = seen + 1;
                return new KeyedAmexRow(row, seen == 0 ? baseId : $"{baseId}-{seen}");
            })];
    }

    /// Amex is the one card shared between owners, so the owner is part of the
    /// key: Alex and Casey can each be charged the same amount by the same
    /// merchant on the same day, and without the owner the second statement
    /// uploaded would lose that row as a false duplicate.
    private static string ContentKey(AmexRow row, string owner) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{owner}|{row.TransactionDate}|{row.ProcessDate}|{row.Description}|{row.Amount}|{(row.IsCredit ? "CR" : "DR")}");

    private static string Hash(string contentKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(contentKey)))[..IdLength];
}

public sealed record KeyedAmexRow(AmexRow Row, string TransactionId);
