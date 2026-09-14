namespace Clam.Api.Features.Statements;

/// The statement columns, once. Feature-shared (tier 2) across list, detail,
/// download and delete — four slices projecting the same row is exactly the
/// case the rule of three is for.
internal static class StatementSql
{
    internal const string Columns = """
        [id], [bank], [owner], [statementDate], [originalName], [contentHash],
        [byteSize], [storageKey], [uploadedAt], [rowCount], [reconciled]
        """;
}
