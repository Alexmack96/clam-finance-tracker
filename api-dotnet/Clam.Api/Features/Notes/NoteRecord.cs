namespace Clam.Api.Features.Notes;

/// A note row. Feature-shared (tier 2) on the rule of three: list, create and
/// update all return exactly this, and nothing about a note differs by slice.
public sealed class NoteRecord
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public bool Pinned { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// The column list, once. Three slices project the same six columns and a
/// mismatch between them is a null property nobody notices until it renders.
internal static class NoteSql
{
    internal const string Columns = "[id], [title], [body], [pinned], [createdAt], [updatedAt]";

    internal const string Inserted = """
        INSERTED.[id], INSERTED.[title], INSERTED.[body],
        INSERTED.[pinned], INSERTED.[createdAt], INSERTED.[updatedAt]
        """;
}
