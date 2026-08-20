using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Results;
using Clam.Api.Infrastructure.Statements;
using Dapper;

namespace Clam.Api.Features.Import.ImportAmex;

/// Uploads an Amex statement PDF: parse it, prove it, keep it, stage its rows.
///
/// The ordering is the design. Nothing is written until the statement has been
/// shown to be worth writing, because the two cheap rejections — the identical
/// file, and the statement whose rows are all already staged — must not leave a
/// <c>StatementFiles</c> row or an orphaned PDF on the volume behind them.
public sealed class ImportAmexCommand(
    IDbConnectionFactory factory,
    IStatementStore store,
    IIdGenerator ids)
{
    private const string Bank = "amex";
    private const string DefaultOwner = "Alex";

    private static readonly HashSet<string> ValidOwners =
        new(StringComparer.Ordinal) { "Alex", "Casey", "Joint" };

    private const string FindByHashSql =
        "SELECT [id], [originalName], [uploadedAt] FROM [StatementFiles] WHERE [contentHash] = @ContentHash;";

    private const string ExistingIdsSql = "SELECT [transactionId] FROM [AmexTransactions];";

    private const string InsertStatementFileSql = """
        INSERT INTO [StatementFiles]
            ([id], [bank], [owner], [statementDate], [originalName], [contentHash],
             [byteSize], [storageKey], [rowCount], [reconciled])
        VALUES
            (@Id, @Bank, @Owner, @StatementDate, @OriginalName, @ContentHash,
             @ByteSize, @StorageKey, @RowCount, 1);
        """;

    private const string InsertRowSql = """
        INSERT INTO [AmexTransactions]
            ([transactionId], [transactionDate], [processDate], [description], [amount],
             [isCredit], [foreignCurrency], [foreignAmount], [statementDate], [owner],
             [statementFileId])
        VALUES
            (@TransactionId, @TransactionDate, @ProcessDate, @Description, @Amount,
             @IsCredit, @ForeignCurrency, @ForeignAmount, @StatementDate, @Owner,
             @StatementFileId);
        """;

    /// A duplicate row staged before statement tracking existed carries no
    /// statementFileId, so the PDF just stored would account for fewer rows than
    /// it actually covers. Claim the unowned ones rather than dropping the link;
    /// rows already belonging to another statement are left alone, because the
    /// first file to claim a row keeps it.
    ///
    /// The second statement is the one place the old `amex:&lt;id&gt;` string join
    /// survives: those transactions were created before the column existed, so
    /// there is nothing else to match them on.
    private const string ClaimOrphanedSql = """
        UPDATE  [AmexTransactions]
        SET     [statementFileId] = @StatementFileId
        WHERE   [transactionId] IN @Duplicates AND [statementFileId] IS NULL;

        UPDATE  [Transactions]
        SET     [statementFileId] = @StatementFileId
        WHERE   [externalId] IN @ExternalIds AND [statementFileId] IS NULL;
        """;

    public async Task<Result<ImportAmexResponse>> ExecuteAsync(ImportAmexRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.File is null || request.File.Length == 0)
            return Result<ImportAmexResponse>.Invalid(new ValidationError("file", "No file uploaded"));

        var pdf = await ReadAsync(request.File, ct);
        var owner = request.Owner is not null && ValidOwners.Contains(request.Owner)
            ? request.Owner
            : DefaultOwner;

        using var connection = await factory.OpenAsync(ct);

        // Hash first. The identical PDF re-uploaded is caught here, before
        // parsing, and independently of how row ids happen to be derived.
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(pdf));
        var existingFile = await connection.QuerySingleOrDefaultAsync<StoredFile>(
            new CommandDefinition(FindByHashSql, new { ContentHash = contentHash }, cancellationToken: ct));

        if (existingFile is not null)
        {
            var uploaded = existingFile.UploadedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Result<ImportAmexResponse>.Conflict(
                $"This exact PDF was already uploaded on {uploaded} as \"{existingFile.OriginalName}\".");
        }

        var parsed = AmexStatementParser.Parse(pdf);
        if (!parsed.Ok) return Rejected(parsed);

        var staged = (await connection.QueryAsync<string>(
            new CommandDefinition(ExistingIdsSql, cancellationToken: ct))).ToHashSet(StringComparer.Ordinal);

        var keyed = AmexBusinessKeys.Assign(parsed.Rows, owner);
        var toInsert = keyed.Where(r => !staged.Contains(r.TransactionId)).ToList();
        var duplicates = keyed.Select(r => r.TransactionId).Where(staged.Contains).ToList();

        // The content hash above catches the identical file; this catches the
        // same statement arriving as different bytes — re-downloaded, or
        // re-rendered. Either way it adds no rows, so it must not leave a
        // statement row or a PDF behind.
        if (toInsert.Count == 0)
        {
            return Result<ImportAmexResponse>.Conflict(
                $"Every transaction on this statement ({duplicates.Count}) is already imported, so nothing was stored.");
        }

        var storageKey = store.KeyFor(Bank, owner, parsed.StatementDate, contentHash);
        var statementFileId = ids.NewId();

        // Bytes before rows. A failure here leaves nothing; a failure after it
        // leaves a file with no row pointing at it, which the next upload of the
        // same PDF simply overwrites — the key is derived from the hash, so it
        // is the same key. The reverse order would leave a row pointing at a
        // file that does not exist, which nothing repairs.
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
                RowCount = parsed.Rows.Count,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(
                InsertRowSql,
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
                    ExternalIds = duplicates.Select(d => $"amex:{d}").ToList(),
                }, transaction, cancellationToken: ct));
            }

            transaction.Commit();
        }

        return Result<ImportAmexResponse>.Success(new ImportAmexResponse
        {
            Imported = toInsert.Count,
            Duplicates = duplicates,
            StatementFileId = statementFileId,
        });
    }

    /// The parser reports the HTTP status it wants, because "unreadable" and
    /// "read fine but does not add up" are genuinely different answers. Mapping
    /// them here keeps that distinction without teaching the parser about
    /// Ardalis.
    private static Result<ImportAmexResponse> Rejected(AmexParseResult parsed) => parsed.Status switch
    {
        StatusCodes.Status400BadRequest =>
            Result<ImportAmexResponse>.Invalid(new ValidationError("file", parsed.Error!)),
        StatusCodes.Status409Conflict => Result<ImportAmexResponse>.Conflict(parsed.Error!),
        _ => Result<ImportAmexResponse>.Error(parsed.Error!),
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
