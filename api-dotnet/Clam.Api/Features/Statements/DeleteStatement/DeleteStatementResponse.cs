namespace Clam.Api.Features.Statements.DeleteStatement;

/// What was destroyed, so the client can say so rather than "done".
public sealed class DeleteStatementResponse
{
    public DeletedStatement Deleted { get; set; } = new();
}

public sealed class DeletedStatement
{
    /// The original filename, which is what the user recognises the statement by.
    public string Statement { get; set; } = "";

    public int Staged { get; set; }
    public int Transactions { get; set; }
}
