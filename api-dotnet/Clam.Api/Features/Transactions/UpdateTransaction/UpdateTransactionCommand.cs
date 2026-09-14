using Ardalis.Result;
using Clam.Api.Domain;
using Clam.Api.Domain.Rules;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Transactions.UpdateTransaction;

public sealed class UpdateTransactionCommand(IDbConnectionFactory factory)
{
    /// `@NoteProvided` rather than `COALESCE(@Note, [note])`, because clearing a
    /// note means sending null and COALESCE cannot tell that from an absent field.
    private const string UpdateSql = """
        UPDATE  [Transactions]
        SET     [note]           = CASE WHEN @NoteProvided = 1 THEN @Note ELSE [note] END,
                [categoryId]     = COALESCE(@CategoryId, [categoryId]),
                [owner]          = COALESCE(@Owner, [owner]),
                [reviewed]       = COALESCE(@Reviewed, [reviewed]),
                [bucket]         = COALESCE(@Bucket, [bucket])
        WHERE   [id] = @Id;
        """;

    private const string SelectSql = """
        SELECT  t.[id], t.[description], t.[amount], t.[type], t.[date], t.[createdAt],
                t.[categoryId], t.[externalId], t.[note], t.[owner], t.[reviewed],
                t.[bucket], t.[originalAmount], t.[originalCurrency], t.[statementFileId],
                c.[id], c.[name], c.[color]
        FROM    [Transactions] t
        JOIN    [Categories] c ON c.[id] = t.[categoryId]
        WHERE   t.[id] = @Id;
        """;

    /// What the owner check and the bucket rerun need to know about the row as
    /// it stands. Only read when the request touches one of those.
    private const string CurrentSql = """
        SELECT  [description], [type], [externalId]
        FROM    [Transactions]
        WHERE   [id] = @Id;
        """;

    public async Task<Result<UpdateTransactionResponse>> ExecuteAsync(
        UpdateTransactionRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);

        var bucket = request.Bucket;
        var rerunBuckets = request.CategoryId is not null && request.Bucket is null;

        if (request.Owner == Owner.Joint || rerunBuckets)
        {
            var current = await connection.QuerySingleOrDefaultAsync<CurrentRow>(
                new CommandDefinition(CurrentSql, new { request.Id }, cancellationToken: ct));

            if (current is null) return Result<UpdateTransactionResponse>.NotFound("Transaction not found");

            var bank = RuleEngine.BankOf(current.ExternalId);

            // Joint is the shared Monzo account. Every other source is one
            // person's card or account, so Joint there is a mis-click.
            if (request.Owner == Owner.Joint && bank != "monzo")
            {
                return Result<UpdateTransactionResponse>.Invalid(
                    new ValidationError("owner", "Only Monzo transactions can be Joint"));
            }

            // A new category re-runs the Bucket rules for this row, so the bucket
            // follows the category the way it would on import or on "Apply".
            // Every rule runs, not just the category's default, so an exception
            // ranked above it (Transport + uber → Wants) still wins. No match
            // leaves the bucket as it was, which is what "Apply" does too. A
            // bucket sent in the same request is the caller's choice and wins.
            if (rerunBuckets)
            {
                var rules = await RuleStore.LoadRulesAsync(connection, ct);
                var categoryNameById = await RuleStore.LoadCategoryNamesAsync(connection, ct);

                var basis = new MatchableTransaction(
                    current.Description,
                    current.Type,
                    categoryNameById.GetValueOrDefault(request.CategoryId!),
                    bank);

                bucket = RuleEngine.ResolveRule(basis, [.. rules.Where(r => r.Kind == RuleKind.Bucket)])?.Bucket;
            }
        }

        var parameters = new
        {
            request.Id,
            Note = request.NoteValue,
            request.NoteProvided,
            request.CategoryId,
            Owner = request.Owner?.ToString(),
            request.Reviewed,
            Bucket = bucket?.ToString(),
        };

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

    private sealed class CurrentRow
    {
        public string Description { get; set; } = "";
        public TransactionType Type { get; set; }
        public string? ExternalId { get; set; }
    }
}
