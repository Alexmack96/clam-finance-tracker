using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Transactions.UpdateTransaction;

public sealed class UpdateTransactionCommand(IDbConnectionFactory factory)
{
    /// Editing a field by hand pins it, so "Run all rules" can never stamp over
    /// the choice. An explicit pin flag in the same request still wins — that is
    /// how a row gets handed back to the rules engine, which is why the pin
    /// columns COALESCE the request's flag *over* the implicit pin.
    ///
    /// `@NoteProvided` rather than `COALESCE(@Note, [note])`, because clearing a
    /// note means sending null and COALESCE cannot tell that from an absent field.
    private const string UpdateSql = """
        UPDATE  [Transactions]
        SET     [note]           = CASE WHEN @NoteProvided = 1 THEN @Note ELSE [note] END,
                [categoryId]     = COALESCE(@CategoryId, [categoryId]),
                [owner]          = COALESCE(@Owner, [owner]),
                [reviewed]       = COALESCE(@Reviewed, [reviewed]),
                [bucket]         = COALESCE(@Bucket, [bucket]),
                [categoryPinned] = COALESCE(@CategoryPinned,
                                       CASE WHEN @CategoryId IS NOT NULL THEN 1 ELSE [categoryPinned] END),
                [bucketPinned]   = COALESCE(@BucketPinned,
                                       CASE WHEN @Bucket IS NOT NULL THEN 1 ELSE [bucketPinned] END)
        WHERE   [id] = @Id;
        """;

    private const string SelectSql = """
        SELECT  t.[id], t.[description], t.[amount], t.[type], t.[date], t.[createdAt],
                t.[categoryId], t.[externalId], t.[note], t.[owner], t.[reviewed],
                t.[bucket], t.[categoryPinned], t.[bucketPinned], t.[originalAmount],
                t.[originalCurrency], t.[statementFileId],
                c.[id], c.[name], c.[color]
        FROM    [Transactions] t
        JOIN    [Categories] c ON c.[id] = t.[categoryId]
        WHERE   t.[id] = @Id;
        """;

    public async Task<Result<UpdateTransactionResponse>> ExecuteAsync(
        UpdateTransactionRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = new
        {
            request.Id,
            Note = request.NoteValue,
            request.NoteProvided,
            request.CategoryId,
            Owner = request.Owner?.ToString(),
            request.Reviewed,
            Bucket = request.Bucket?.ToString(),
            request.CategoryPinned,
            request.BucketPinned,
        };

        using var connection = await factory.OpenAsync(ct);

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(UpdateSql, parameters, cancellationToken: ct));

        if (affected == 0) return Result<UpdateTransactionResponse>.NotFound("Transaction not found");

        var rows = await connection.QueryAsync<UpdateTransactionResponse, UpdatedTransactionCategory, UpdateTransactionResponse>(
            new CommandDefinition(SelectSql, new { request.Id }, cancellationToken: ct),
            (transaction, category) =>
            {
                transaction.Category = category;
                return transaction;
            },
            splitOn: "id");

        var updated = rows.FirstOrDefault();
        return updated is null
            ? Result<UpdateTransactionResponse>.NotFound("Transaction not found")
            : Result<UpdateTransactionResponse>.Success(updated);
    }
}
