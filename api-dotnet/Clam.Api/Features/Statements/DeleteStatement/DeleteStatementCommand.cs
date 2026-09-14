using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Statements;
using Dapper;

namespace Clam.Api.Features.Statements.DeleteStatement;

public sealed class DeleteStatementCommand(IDbConnectionFactory factory, IStatementStore store)
{
    private const string SelectSql =
        "SELECT [storageKey], [originalName] FROM [StatementFiles] WHERE [id] = @Id;";

    /// The transactions are deleted explicitly rather than left to the foreign
    /// key, which is ON DELETE SET NULL by design: orphaning derived rows is the
    /// safe default, destroying them is a decision this slice makes on purpose.
    ///
    /// All three go in one transaction so a failure cannot strip the
    /// transactions and leave the statement looking intact.
    /// Every staging table that can carry a statementFileId is cleared here, not
    /// just the bank the file happens to belong to — a statement row points at
    /// one bank's rows, but this slice should not have to know which, and a bank
    /// added to the pipeline without being added here would silently leave its
    /// staged rows behind.
    private const string DeleteSql = """
        DELETE FROM [Transactions] WHERE [statementFileId] = @Id;
        SELECT @@ROWCOUNT AS [Transactions];

        DECLARE @staged INT = 0;

        DELETE FROM [AmexTransactions] WHERE [statementFileId] = @Id;
        SET @staged = @staged + @@ROWCOUNT;

        DELETE FROM [BarclaysTransactions] WHERE [statementFileId] = @Id;
        SET @staged = @staged + @@ROWCOUNT;

        DELETE FROM [HsbcTransactions] WHERE [statementFileId] = @Id;
        SET @staged = @staged + @@ROWCOUNT;

        DELETE FROM [SantanderTransactions] WHERE [statementFileId] = @Id;
        SET @staged = @staged + @@ROWCOUNT;

        DELETE FROM [ChaseTransactions] WHERE [statementFileId] = @Id;
        SET @staged = @staged + @@ROWCOUNT;

        DELETE FROM [SofiTransactions] WHERE [statementFileId] = @Id;
        SET @staged = @staged + @@ROWCOUNT;

        SELECT @staged AS [Staged];

        DELETE FROM [StatementFiles] WHERE [id] = @Id;
        """;

    public async Task<Result<DeleteStatementResponse>> ExecuteAsync(
        DeleteStatementRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);

        var file = await connection.QuerySingleOrDefaultAsync<StoredFile>(
            new CommandDefinition(SelectSql, new { request.Id }, cancellationToken: ct));

        if (file is null) return Result<DeleteStatementResponse>.NotFound("Statement not found");

        using var transaction = connection.BeginTransaction();

        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(DeleteSql, new { request.Id }, transaction, cancellationToken: ct));

        var transactions = await grid.ReadSingleAsync<int>();
        var staged = await grid.ReadSingleAsync<int>();

        transaction.Commit();

        // After the commit: if unlinking fails the database is already
        // consistent, and an orphaned file on the volume is harmless.
        await store.RemoveAsync(file.StorageKey, ct);

        return Result<DeleteStatementResponse>.Success(new DeleteStatementResponse
        {
            Deleted = new DeletedStatement
            {
                Statement = file.OriginalName,
                Staged = staged,
                Transactions = transactions,
            },
        });
    }

    private sealed class StoredFile
    {
        public string StorageKey { get; set; } = "";
        public string OriginalName { get; set; } = "";
    }
}
