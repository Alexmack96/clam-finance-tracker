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

    public string KeyFor(string bank, string owner, string? statementDate, string contentHash)
    {
        ArgumentNullException.ThrowIfNull(contentHash);

        // The hash suffix keeps keys unique when two statements share a date, and
        // makes the file self-identifying if you are poking around on the volume.
        var date = string.IsNullOrWhiteSpace(statementDate) ? "undated" : Slug(statementDate);
        var hash = Slug(contentHash);
        return $"{Slug(bank)}/{Slug(owner)}/{date}-{hash[..Math.Min(12, hash.Length)]}.pdf";
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
