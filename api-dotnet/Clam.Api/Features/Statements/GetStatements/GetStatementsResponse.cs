namespace Clam.Api.Features.Statements.GetStatements;

/// A statement in the list, with the staged-row count Prisma's
/// `_count: { amexRows: true }` produces, flattened to `stagedRows`.
public sealed class StatementListItem
{
    public string Id { get; set; } = "";
    public string Bank { get; set; } = "";
    public string Owner { get; set; } = "";
    public string? StatementDate { get; set; }
    public string OriginalName { get; set; } = "";

    /// SHA-256 of the bytes. Exposed so a duplicate upload can be explained
    /// rather than merely rejected.
    public string ContentHash { get; set; } = "";

    public int ByteSize { get; set; }
    public string StorageKey { get; set; } = "";
    public DateTime UploadedAt { get; set; }

    /// What the parser said it found. Null for statements uploaded before the
    /// count was recorded; distinct from <see cref="StagedRows"/>, which is what
    /// is actually in staging now.
    public int? RowCount { get; set; }

    public bool Reconciled { get; set; }
    public int StagedRows { get; set; }
}

/// A bare JSON array, matching `res.json(files.map(...))`.
public sealed class GetStatementsResponse : List<StatementListItem>
{
    public GetStatementsResponse(IEnumerable<StatementListItem> statements) : base(statements) { }
}
