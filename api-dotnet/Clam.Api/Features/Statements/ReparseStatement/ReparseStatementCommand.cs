using System.Data;
using Ardalis.Result;
using Clam.Api.Features.Import.ImportAmex;
using Clam.Api.Features.Import.ImportBarclays;
using Clam.Api.Features.Import.ImportChase;
using Clam.Api.Features.Import.ImportHsbc;
using Clam.Api.Features.Import.ImportSantander;
using Clam.Api.Features.Import.ImportSofi;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Statements;
using Dapper;

namespace Clam.Api.Features.Statements.ReparseStatement;

/// Re-runs the current parser over the bytes already on the volume. This is the
/// payoff for keeping the PDFs: fixing a parser bug no longer means finding the
/// original file and uploading it again.
///
/// Two orderings are load-bearing and neither is arbitrary:
///
/// 1. **Parse before deleting.** A statement that no longer parses has to leave
///    its existing rows alone. Deleting first would destroy a good import in
///    exchange for a failed one.
/// 2. **Delete inside the same transaction as the re-stage.** Otherwise a
///    failure half way through leaves the statement holding nothing, which
///    looks identical to a statement that legitimately parsed to zero rows.
///
/// Unlike the Express route this shadows, every bank with a parser is
/// re-parseable — that route refuses all but Amex because Amex was the only
/// parser it had, which is the absence of a parser rather than a policy.
public sealed class ReparseStatementCommand(IDbConnectionFactory factory, IStatementStore store)
{
    private const string SelectSql = """
        SELECT [id], [bank], [owner], [storageKey]
        FROM   [StatementFiles]
        WHERE  [id] = @Id;
        """;

    /// The staging table is chosen from the statement's own bank rather than
    /// cleared across all of them: a re-parse rewrites one statement, and a bank
    /// this slice cannot parse never gets this far.
    private const string DeleteDerivedSql = """
        DELETE FROM [Transactions] WHERE [statementFileId] = @Id;
        SELECT @@ROWCOUNT AS [Transactions];

        DELETE FROM {0} WHERE [statementFileId] = @Id;
        SELECT @@ROWCOUNT AS [Staged];
        """;

    private const string UpdateStatementSql = """
        UPDATE [StatementFiles]
        SET    [rowCount] = @RowCount, [statementDate] = @StatementDate, [reconciled] = 1
        WHERE  [id] = @Id;
        """;

    private const string AmexInsertSql = """
        INSERT INTO [AmexTransactions]
            ([transactionId], [transactionDate], [processDate], [description], [amount],
             [isCredit], [foreignCurrency], [foreignAmount], [statementDate], [owner], [statementFileId])
        VALUES
            (@TransactionId, @TransactionDate, @ProcessDate, @Description, @Amount,
             @IsCredit, @ForeignCurrency, @ForeignAmount, @StatementDate, @Owner, @StatementFileId);
        """;

    private const string BarclaysInsertSql = """
        INSERT INTO [BarclaysTransactions]
            ([transactionId], [date], [description], [amount], [isCredit],
             [statementDate], [owner], [statementFileId])
        VALUES
            (@TransactionId, @Date, @Description, @Amount, @IsCredit,
             @StatementDate, @Owner, @StatementFileId);
        """;

    private const string SofiInsertSql = """
        INSERT INTO [SofiTransactions]
            ([transactionId], [date], [type], [description], [amount], [isCredit],
             [balance], [accountType], [statementDate], [owner], [statementFileId])
        VALUES
            (@TransactionId, @Date, @Type, @Description, @Amount, @IsCredit,
             @Balance, @AccountType, @StatementDate, @Owner, @StatementFileId);
        """;

    private const string ChaseInsertSql = """
        INSERT INTO [ChaseTransactions]
            ([transactionId], [date], [description], [amount], [isCredit],
             [statementDate], [owner], [statementFileId])
        VALUES
            (@TransactionId, @Date, @Description, @Amount, @IsCredit,
             @StatementDate, @Owner, @StatementFileId);
        """;

    private const string SantanderInsertSql = """
        INSERT INTO [SantanderTransactions]
            ([transactionId], [date], [description], [moneyIn], [moneyOut], [balance],
             [statementDate], [owner], [statementFileId])
        VALUES
            (@TransactionId, @Date, @Description, @MoneyIn, @MoneyOut, @Balance,
             @StatementDate, @Owner, @StatementFileId);
        """;

    private const string HsbcInsertSql = """
        INSERT INTO [HsbcTransactions]
            ([transactionId], [date], [paymentType], [description], [moneyOut], [moneyIn],
             [balance], [statementDate], [owner], [statementFileId])
        VALUES
            (@TransactionId, @Date, @PaymentType, @Description, @MoneyOut, @MoneyIn,
             @Balance, @StatementDate, @Owner, @StatementFileId);
        """;

    public async Task<Result<ReparseStatementResponse>> ExecuteAsync(
        ReparseStatementRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);

        var file = await connection.QuerySingleOrDefaultAsync<StoredFile>(
            new CommandDefinition(SelectSql, new { request.Id }, cancellationToken: ct));

        if (file is null) return Result<ReparseStatementResponse>.NotFound("Statement not found");

        if (file.Bank is not ("amex" or "barclays" or "chase" or "hsbc" or "santander" or "sofi"))
        {
            return Result<ReparseStatementResponse>.Invalid(new ValidationError(
                "bank", $"Re-parse is not supported for {file.Bank} statements yet"));
        }

        byte[] pdf;
        try
        {
            pdf = await store.ReadAsync(file.StorageKey, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Unavailable, which the endpoint re-maps to 410: the row exists but
            // the bytes are gone, which is permanent rather than a retry.
            return Result<ReparseStatementResponse>.Unavailable(
                "The stored PDF for this statement is missing from the volume");
        }

        return file.Bank switch
        {
            "amex" => await ReparseAmexAsync(connection, file, pdf, ct),
            "barclays" => await ReparseBarclaysAsync(connection, file, pdf, ct),
            "chase" => await ReparseChaseAsync(connection, file, pdf, ct),
            "santander" => await ReparseSantanderAsync(connection, file, pdf, ct),
            "sofi" => await ReparseSofiAsync(connection, file, pdf, ct),
            _ => await ReparseHsbcAsync(connection, file, pdf, ct),
        };
    }

    private async Task<Result<ReparseStatementResponse>> ReparseAmexAsync(
        IDbConnection connection,
        StoredFile file,
        byte[] pdf,
        CancellationToken ct)
    {
        var parsed = AmexStatementParser.Parse(pdf);
        if (!parsed.Ok) return Rejected(parsed.Status, parsed.Error!);

        var keyed = AmexBusinessKeys.Assign(parsed.Rows, file.Owner);

        using var transaction = connection.BeginTransaction();

        var removed = await ClearAsync(connection, transaction, "[AmexTransactions]", file.Id, ct);

        // Read the surviving ids *after* the delete: this statement's own rows
        // are gone by now, so anything still matching belongs to another file.
        var staged = await ExistingIdsAsync(connection, transaction, "[AmexTransactions]", ct);

        var toInsert = keyed.Where(r => !staged.Contains(r.TransactionId)).ToList();
        var duplicates = keyed.Select(r => r.TransactionId).Where(staged.Contains).ToList();

        if (toInsert.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                AmexInsertSql,
                toInsert.Select(r => new
                {
                    r.TransactionId,
                    r.Row.TransactionDate,
                    r.Row.ProcessDate,
                    r.Row.Description,
                    r.Row.Amount,
                    r.Row.IsCredit,
                    r.Row.ForeignCurrency,
                    r.Row.ForeignAmount,
                    StatementDate = parsed.StatementDate,
                    file.Owner,
                    StatementFileId = file.Id,
                }).ToList(),
                transaction,
                cancellationToken: ct));
        }

        await FinishAsync(connection, transaction, file.Id, parsed.Rows.Count, parsed.StatementDate, ct);
        transaction.Commit();

        return Success(removed, toInsert.Count, duplicates);
    }

    private async Task<Result<ReparseStatementResponse>> ReparseBarclaysAsync(
        IDbConnection connection,
        StoredFile file,
        byte[] pdf,
        CancellationToken ct)
    {
        var parsed = BarclaysStatementParser.Parse(pdf);
        if (!parsed.Ok) return Rejected(parsed.Status, parsed.Error!);

        var keyed = BarclaysBusinessKeys.Assign(parsed.Rows);

        using var transaction = connection.BeginTransaction();

        var removed = await ClearAsync(connection, transaction, "[BarclaysTransactions]", file.Id, ct);

        // Barclaycard's [transactionId] is nullable, so the surviving ids are
        // read with the NULLs filtered out — a NULL could not collide with a
        // content hash and HashSet<string> has no room for it.
        var staged = await ExistingIdsAsync(connection, transaction, "[BarclaysTransactions]", ct);

        var toInsert = keyed.Where(r => !staged.Contains(r.TransactionId)).ToList();
        var duplicates = keyed.Select(r => r.TransactionId).Where(staged.Contains).ToList();

        if (toInsert.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                BarclaysInsertSql,
                toInsert.Select(r => new
                {
                    r.TransactionId,
                    r.Row.Date,
                    r.Row.Description,
                    r.Row.Amount,
                    r.Row.IsCredit,
                    StatementDate = parsed.StatementDate,
                    file.Owner,
                    StatementFileId = file.Id,
                }).ToList(),
                transaction,
                cancellationToken: ct));
        }

        await FinishAsync(connection, transaction, file.Id, parsed.Rows.Count, parsed.StatementDate, ct);
        transaction.Commit();

        return Success(removed, toInsert.Count, duplicates);
    }

    private async Task<Result<ReparseStatementResponse>> ReparseHsbcAsync(
        IDbConnection connection,
        StoredFile file,
        byte[] pdf,
        CancellationToken ct)
    {
        var parsed = HsbcStatementParser.Parse(pdf);
        if (!parsed.Ok) return Rejected(parsed.Status, parsed.Error!);

        var keyed = HsbcBusinessKeys.Assign(parsed.Rows);

        using var transaction = connection.BeginTransaction();

        var removed = await ClearAsync(connection, transaction, "[HsbcTransactions]", file.Id, ct);
        var staged = await ExistingIdsAsync(connection, transaction, "[HsbcTransactions]", ct);

        var toInsert = keyed.Where(r => !staged.Contains(r.TransactionId)).ToList();
        var duplicates = keyed.Select(r => r.TransactionId).Where(staged.Contains).ToList();

        if (toInsert.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                HsbcInsertSql,
                toInsert.Select(r => new
                {
                    r.TransactionId,
                    r.Row.Date,
                    r.Row.PaymentType,
                    r.Row.Description,
                    r.Row.MoneyOut,
                    r.Row.MoneyIn,
                    r.Row.Balance,
                    StatementDate = parsed.StatementDate,
                    file.Owner,
                    StatementFileId = file.Id,
                }).ToList(),
                transaction,
                cancellationToken: ct));
        }

        await FinishAsync(connection, transaction, file.Id, parsed.Rows.Count, parsed.StatementDate, ct);
        transaction.Commit();

        return Success(removed, toInsert.Count, duplicates);
    }

    private async Task<Result<ReparseStatementResponse>> ReparseSofiAsync(
        IDbConnection connection,
        StoredFile file,
        byte[] pdf,
        CancellationToken ct)
    {
        var parsed = SofiStatementParser.Parse(pdf);
        if (!parsed.Ok) return Rejected(parsed.Status, parsed.Error!);

        var keyed = SofiBusinessKeys.Assign(parsed.Rows);

        using var transaction = connection.BeginTransaction();

        var removed = await ClearAsync(connection, transaction, "[SofiTransactions]", file.Id, ct);
        var staged = await ExistingIdsAsync(connection, transaction, "[SofiTransactions]", ct);

        var toInsert = keyed.Where(r => !staged.Contains(r.TransactionId)).ToList();
        var duplicates = keyed.Select(r => r.TransactionId).Where(staged.Contains).ToList();

        if (toInsert.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                SofiInsertSql,
                toInsert.Select(r => new
                {
                    r.TransactionId,
                    r.Row.Date,
                    r.Row.Type,
                    r.Row.Description,
                    r.Row.Amount,
                    r.Row.IsCredit,
                    r.Row.Balance,
                    r.Row.AccountType,
                    StatementDate = parsed.StatementDate,
                    file.Owner,
                    StatementFileId = file.Id,
                }).ToList(),
                transaction,
                cancellationToken: ct));
        }

        await FinishAsync(connection, transaction, file.Id, parsed.Rows.Count, parsed.StatementDate, ct);
        transaction.Commit();

        return Success(removed, toInsert.Count, duplicates);
    }

    private async Task<Result<ReparseStatementResponse>> ReparseChaseAsync(
        IDbConnection connection,
        StoredFile file,
        byte[] pdf,
        CancellationToken ct)
    {
        var parsed = ChaseStatementParser.Parse(pdf);
        if (!parsed.Ok) return Rejected(parsed.Status, parsed.Error!);

        var keyed = ChaseBusinessKeys.Assign(parsed.Rows);

        using var transaction = connection.BeginTransaction();

        var removed = await ClearAsync(connection, transaction, "[ChaseTransactions]", file.Id, ct);
        var staged = await ExistingIdsAsync(connection, transaction, "[ChaseTransactions]", ct);

        var toInsert = keyed.Where(r => !staged.Contains(r.TransactionId)).ToList();
        var duplicates = keyed.Select(r => r.TransactionId).Where(staged.Contains).ToList();

        if (toInsert.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                ChaseInsertSql,
                toInsert.Select(r => new
                {
                    r.TransactionId,
                    r.Row.Date,
                    r.Row.Description,
                    r.Row.Amount,
                    r.Row.IsCredit,
                    StatementDate = parsed.StatementDate,
                    file.Owner,
                    StatementFileId = file.Id,
                }).ToList(),
                transaction,
                cancellationToken: ct));
        }

        await FinishAsync(connection, transaction, file.Id, parsed.Rows.Count, parsed.StatementDate, ct);
        transaction.Commit();

        return Success(removed, toInsert.Count, duplicates);
    }

    private async Task<Result<ReparseStatementResponse>> ReparseSantanderAsync(
        IDbConnection connection,
        StoredFile file,
        byte[] pdf,
        CancellationToken ct)
    {
        var parsed = SantanderStatementParser.Parse(pdf);
        if (!parsed.Ok) return Rejected(parsed.Status, parsed.Error!);

        var keyed = SantanderBusinessKeys.Assign(parsed.Rows);

        using var transaction = connection.BeginTransaction();

        var removed = await ClearAsync(connection, transaction, "[SantanderTransactions]", file.Id, ct);
        var staged = await ExistingIdsAsync(connection, transaction, "[SantanderTransactions]", ct);

        var toInsert = keyed.Where(r => !staged.Contains(r.TransactionId)).ToList();
        var duplicates = keyed.Select(r => r.TransactionId).Where(staged.Contains).ToList();

        if (toInsert.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                SantanderInsertSql,
                toInsert.Select(r => new
                {
                    r.TransactionId,
                    r.Row.Date,
                    r.Row.Description,
                    r.Row.MoneyIn,
                    r.Row.MoneyOut,
                    r.Row.Balance,
                    StatementDate = parsed.StatementDate,
                    file.Owner,
                    StatementFileId = file.Id,
                }).ToList(),
                transaction,
                cancellationToken: ct));
        }

        await FinishAsync(connection, transaction, file.Id, parsed.Rows.Count, parsed.StatementDate, ct);
        transaction.Commit();

        return Success(removed, toInsert.Count, duplicates);
    }

    private static async Task<RemovedByReparse> ClearAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string table,
        string id,
        CancellationToken ct)
    {
        // The table name is interpolated, never the id: it comes from this
        // slice's own two constants, so there is nothing here a request can
        // reach. The id stays a parameter.
        var sql = string.Format(System.Globalization.CultureInfo.InvariantCulture, DeleteDerivedSql, table);

        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: ct));

        var transactions = await grid.ReadSingleAsync<int>();
        var staged = await grid.ReadSingleAsync<int>();

        return new RemovedByReparse { Staged = staged, Transactions = transactions };
    }

    private static async Task<HashSet<string>> ExistingIdsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string table,
        CancellationToken ct)
    {
        // NULLs excluded: Barclaycard's column is nullable, and a null id is
        // neither a duplicate of anything nor something a HashSet can hold.
        var sql = $"SELECT [transactionId] FROM {table} WHERE [transactionId] IS NOT NULL;";
        var ids = await connection.QueryAsync<string>(
            new CommandDefinition(sql, transaction: transaction, cancellationToken: ct));

        return ids.ToHashSet(StringComparer.Ordinal);
    }

    private static Task FinishAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string id,
        int rowCount,
        string? statementDate,
        CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            UpdateStatementSql,
            new { Id = id, RowCount = rowCount, StatementDate = statementDate },
            transaction,
            cancellationToken: ct));

    private static Result<ReparseStatementResponse> Success(
        RemovedByReparse removed,
        int imported,
        IReadOnlyList<string> duplicates) =>
        Result<ReparseStatementResponse>.Success(new ReparseStatementResponse
        {
            Removed = removed,
            Imported = imported,
            Duplicates = duplicates,
        });

    /// The parser's own status is kept: a PDF that cannot be read (400) is a
    /// different answer from one that read fine but does not reconcile (422),
    /// and that split is the reason the parsers return a status at all.
    private static Result<ReparseStatementResponse> Rejected(int status, string error) => status switch
    {
        StatusCodes.Status400BadRequest =>
            Result<ReparseStatementResponse>.Invalid(new ValidationError("file", error)),
        StatusCodes.Status409Conflict => Result<ReparseStatementResponse>.Conflict(error),
        _ => Result<ReparseStatementResponse>.Error(error),
    };

    private sealed class StoredFile
    {
        public string Id { get; set; } = "";
        public string Bank { get; set; } = "";
        public string Owner { get; set; } = "";
        public string StorageKey { get; set; } = "";
    }
}
