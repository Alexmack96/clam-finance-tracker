using System.Globalization;
using System.Security.Cryptography;
using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Statements;
using Dapper;

namespace Clam.Api.Features.Import.ImportHsbc;

/// Uploads an HSBC statement PDF: parse it, prove it, keep it, stage its rows.
///
/// Same shape and same ordering as the Amex slice — nothing is written until the
/// statement has been shown to be worth writing, so neither of the two cheap
/// rejections can leave a <c>StatementFiles</c> row or an orphaned PDF behind.
/// The duplication between the two is deliberate under the tiering rule; what
/// they genuinely share (the positional grid) has been promoted, and the rest is
/// each bank's own business.
public sealed class ImportHsbcCommand(
    IDbConnectionFactory factory,
    IStatementStore store,
    IIdGenerator ids,
    TimeProvider clock)
{
    private const string Bank = "hsbc";
    private const string DefaultOwner = "Joint";

    private static readonly HashSet<string> ValidOwners =
        new(StringComparer.Ordinal) { "Alex", "Casey", "Joint" };

    private const string FindByHashSql =
        "SELECT [id], [originalName], [uploadedAt] FROM [StatementFiles] WHERE [contentHash] = @ContentHash;";

    private const string ExistingIdsSql = "SELECT [transactionId] FROM [HsbcTransactions];";

    private const string InsertStatementFileSql = """
        INSERT INTO [StatementFiles]
            ([id], [bank], [owner], [statementDate], [originalName], [contentHash],
             [byteSize], [storageKey], [uploadedAt], [rowCount], [reconciled])
        VALUES
            (@Id, @Bank, @Owner, @StatementDate, @OriginalName, @ContentHash,
             @ByteSize, @StorageKey, @UploadedAt, @RowCount, 1);
        """;

    private const string InsertRowSql = """
        INSERT INTO [HsbcTransactions]
            ([transactionId], [date], [paymentType], [description], [moneyOut], [moneyIn],
             [balance], [statementDate], [owner], [statementFileId])
        VALUES
            (@TransactionId, @Date, @PaymentType, @Description, @MoneyOut, @MoneyIn,
             @Balance, @StatementDate, @Owner, @StatementFileId);
        """;

    /// See the Amex equivalent: a duplicate row staged before statement tracking
    /// existed carries no statementFileId, so the PDF just stored would account
    /// for fewer rows than it covers. Claim the unowned ones; rows already
    /// belonging to another statement keep it, first file wins.
    private const string ClaimOrphanedSql = """
        UPDATE  [HsbcTransactions]
        SET     [statementFileId] = @StatementFileId
        WHERE   [transactionId] IN @Duplicates AND [statementFileId] IS NULL;

        UPDATE  [Transactions]
        SET     [statementFileId] = @StatementFileId
        WHERE   [externalId] IN @ExternalIds AND [statementFileId] IS NULL;
        """;

    public async Task<Result<ImportHsbcResponse>> ExecuteAsync(ImportHsbcRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.File is null || request.File.Length == 0)
            return Result<ImportHsbcResponse>.Invalid(new ValidationError("file", "No file uploaded"));

        var pdf = await ReadAsync(request.File, ct);
        var owner = request.Owner is not null && ValidOwners.Contains(request.Owner)
            ? request.Owner
            : DefaultOwner;

        using var connection = await factory.OpenAsync(ct);

        var contentHash = Convert.ToHexStringLower(SHA256.HashData(pdf));
        var existingFile = await connection.QuerySingleOrDefaultAsync<StoredFile>(
            new CommandDefinition(FindByHashSql, new { ContentHash = contentHash }, cancellationToken: ct));

        if (existingFile is not null)
        {
            var uploaded = existingFile.UploadedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Result<ImportHsbcResponse>.Conflict(
                $"This exact PDF was already uploaded on {uploaded} as \"{existingFile.OriginalName}\".");
        }

        var parsed = HsbcStatementParser.Parse(pdf);
        if (!parsed.Ok) return Rejected(parsed);

        var staged = (await connection.QueryAsync<string>(
            new CommandDefinition(ExistingIdsSql, cancellationToken: ct))).ToHashSet(StringComparer.Ordinal);

        var keyed = HsbcBusinessKeys.Assign(parsed.Rows);
        var toInsert = keyed.Where(r => !staged.Contains(r.TransactionId)).ToList();
        var duplicates = keyed.Select(r => r.TransactionId).Where(staged.Contains).ToList();

        if (toInsert.Count == 0)
        {
            return Result<ImportHsbcResponse>.Conflict(
                $"Every transaction on this statement ({duplicates.Count}) is already imported, so nothing was stored.");
        }

        var storageKey = store.KeyFor(Bank, owner, parsed.StatementDate, contentHash);
        var statementFileId = ids.NewId();

        // Bytes before rows: a failure here leaves nothing, where the reverse
        // order would leave a row pointing at a file that does not exist.
        await store.SaveAsync(storageKey, pdf, ct);

        using (var transaction = connection.BeginTransaction())
        {
            await connection.ExecuteAsync(new CommandDefinition(InsertStatementFileSql, new
            {
                Id = statementFileId,
                Bank,
                Owner = owner,
                parsed.StatementDate,
                OriginalName = request.File.FileName,
                ContentHash = contentHash,
                ByteSize = pdf.Length,
                StorageKey = storageKey,
                // Stamped from the clock, not left to the column's SYSUTCDATETIME
                // default: the duplicate-upload 409 prints this date back, so a
                // default-stamped row makes that message change by the day.
                UploadedAt = clock.GetUtcNow().UtcDateTime,
                RowCount = parsed.Rows.Count,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(
                InsertRowSql,
                toInsert.Select(r => new
                {
                    r.TransactionId,
                    r.Row.Date,
                    r.Row.PaymentType,
                    r.Row.Description,
                    r.Row.MoneyOut,
                    r.Row.MoneyIn,
                    r.Row.Balance,
                    r.Row.StatementDate,
                    Owner = owner,
                    StatementFileId = statementFileId,
                }).ToList(),
                transaction,
                cancellationToken: ct));

            if (duplicates.Count > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(ClaimOrphanedSql, new
                {
                    StatementFileId = statementFileId,
                    Duplicates = duplicates,
                    ExternalIds = duplicates.Select(d => $"hsbc:{d}").ToList(),
                }, transaction, cancellationToken: ct));
            }

            transaction.Commit();
        }

        return Result<ImportHsbcResponse>.Success(new ImportHsbcResponse
        {
            Imported = toInsert.Count,
            Duplicates = duplicates,
            StatementFileId = statementFileId,
        });
    }

    private static Result<ImportHsbcResponse> Rejected(HsbcParseResult parsed) => parsed.Status switch
    {
        StatusCodes.Status400BadRequest =>
            Result<ImportHsbcResponse>.Invalid(new ValidationError("file", parsed.Error!)),
        StatusCodes.Status409Conflict => Result<ImportHsbcResponse>.Conflict(parsed.Error!),
        _ => Result<ImportHsbcResponse>.Error(parsed.Error!),
    };

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await using (var source = file.OpenReadStream())
        {
            await source.CopyToAsync(buffer, ct);
        }

        return buffer.ToArray();
    }

    private sealed class StoredFile
    {
        public string Id { get; set; } = "";
        public string OriginalName { get; set; } = "";
        public DateTime UploadedAt { get; set; }
    }
}
