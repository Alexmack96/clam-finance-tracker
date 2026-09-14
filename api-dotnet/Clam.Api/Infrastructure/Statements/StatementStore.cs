using System.Globalization;
using System.Text.RegularExpressions;

namespace Clam.Api.Infrastructure.Statements;

public interface IStatementStore
{
    /// Absolute path for a key, guaranteed to sit inside the base directory.
    string PathFor(string key);

    string KeyFor(string bank, string owner, string? statementDate, string contentHash);

    Task SaveAsync(string key, byte[] data, CancellationToken ct = default);

    Task<byte[]> ReadAsync(string key, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);
}

/// Statement PDFs live on disk beside the database, under a `statements/`
/// directory. One volume is then the single thing to back up, and no extra
/// vendor or credential is involved. The trade-off is a shared blast radius:
/// losing the volume loses both the database and the source documents.
public sealed partial class FileSystemStatementStore : IStatementStore
{
    private readonly string _base;

    public FileSystemStatementStore(string baseDirectory)
    {
        _base = Path.GetFullPath(baseDirectory);
    }

    public string PathFor(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        // Never trust a stored key: a tampered or legacy row must not be able to
        // read or delete files outside the statements directory. This is the
        // second of two defences; see Slug for the first.
        if (Path.IsPathRooted(key))
            throw new InvalidOperationException($"Statement key must be relative: {key}");

        var full = Path.GetFullPath(Path.Combine(_base, key));
        if (!full.Equals(_base, StringComparison.Ordinal)
            && !full.StartsWith(_base + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Statement key escapes the statements directory: {key}");
        }

        return full;
    }

    /// `amex/Alex/2026-02-24-amex.pdf`: sorts by date on the volume and reads at a
    /// glance. Only new uploads get this shape. Every read goes through the key
    /// stored on the StatementFiles row, so older files keep their older names.
    public string KeyFor(string bank, string owner, string? statementDate, string contentHash)
    {
        ArgumentNullException.ThrowIfNull(contentHash);

        var date = IsoDate(statementDate)
            ?? (string.IsNullOrWhiteSpace(statementDate) ? "undated" : Slug(statementDate));
        var stem = $"{Slug(bank)}/{Slug(owner)}/{date}-{Slug(bank)}";

        // Two different statements can share a bank, owner and date, a reissued
        // one say. Identical bytes never get this far (contentHash is unique), so
        // a taken name means a different file, and a short hash stops it
        // overwriting the first.
        var key = $"{stem}.pdf";
        if (File.Exists(PathFor(key)))
        {
            var hash = Slug(contentHash);
            key = $"{stem}-{hash[..Math.Min(8, hash.Length)]}.pdf";
        }

        return key;
    }

    private static readonly string[] DayFormats = ["dd/MM/yy", "d MMMM yyyy", "d MMM yyyy"];
    private static readonly string[] MonthFormats = ["MMMM yyyy", "MMM yyyy"];

    /// The labels the parsers produce: "24/02/26" (Amex), "February 2026"
    /// (Barclays), "Feb 2026" (Chase, SoFi), "10 March to 9 April 2026" (HSBC),
    /// "21 Feb 2026 to 20 Mar 2026" (Santander). A range is named by its end, the
    /// day the statement closed. A month-only label stays a month rather than
    /// gaining a day the statement never printed. Null when nothing parses.
    internal static string? IsoDate(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;

        var text = label.Trim();
        var to = text.LastIndexOf(" to ", StringComparison.OrdinalIgnoreCase);
        if (to >= 0) text = text[(to + 4)..].Trim();
        text = text.Replace("Sept ", "Sep ", StringComparison.OrdinalIgnoreCase);

        if (DateOnly.TryParseExact(text, DayFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (DateOnly.TryParseExact(text, MonthFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
            return month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        return null;
    }

    public async Task SaveAsync(string key, byte[] data, CancellationToken ct = default)
    {
        var full = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, data, ct);
    }

    public Task<byte[]> ReadAsync(string key, CancellationToken ct = default) =>
        File.ReadAllBytesAsync(PathFor(key), ct);

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        // Already gone is success — deleting a statement whose file was lost
        // should still clear the database rows.
        var full = PathFor(key);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    /// Path segments are built from user-influenced values (owner, original
    /// filename, statement date), so strip anything that is not safe in a path
    /// component before it ever reaches the filesystem.
    private static string Slug(string value)
    {
        var cleaned = UnsafeChars().Replace(value, "-").Trim('-', '.');
        if (cleaned.Length > 60) cleaned = cleaned[..60];
        return cleaned.Length == 0 ? "unknown" : cleaned;
    }

    [GeneratedRegex("[^A-Za-z0-9._-]+")]
    private static partial Regex UnsafeChars();
}
