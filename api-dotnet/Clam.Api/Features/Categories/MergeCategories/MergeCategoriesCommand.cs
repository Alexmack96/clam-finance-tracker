using Ardalis.Result;
using Clam.Api.Features.Categories.DeleteCategory;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Categories.MergeCategories;

public sealed class MergeCategoriesCommand(IDbConnectionFactory factory)
{
    private const string NameSql = "SELECT [name] FROM [Categories] WHERE [id] = @Id;";

    /// `Rules.categoryId` is ON DELETE CASCADE, so deleting the source without
    /// repointing first would silently destroy every rule that routes into it —
    /// the user asked to merge two categories, not to throw away their rules.
    ///
    /// Bucket rules test the category by name, so exact-match conditions follow
    /// the merge the same way they follow a rename. Partial matches are left
    /// alone for the same reason as there.
    ///
    /// All four statements run in one transaction: a half-applied merge leaves
    /// transactions pointing at a category that no longer exists.
    private const string MergeSql = """
        UPDATE [Transactions] SET [categoryId] = @ToId WHERE [categoryId] = @FromId;
        SELECT @@ROWCOUNT AS [Merged];

        UPDATE [Rules] SET [categoryId] = @ToId WHERE [categoryId] = @FromId;
        SELECT @@ROWCOUNT AS [RulesRepointed];

        UPDATE  [RuleConditions]
        SET     [value] = @ToName
        WHERE   [field] = 'Category' AND [operator] = 'Exact' AND [value] = @FromName;
        SELECT @@ROWCOUNT AS [ConditionsRewritten];

        DELETE FROM [Categories] WHERE [id] = @FromId;
        """;

    public async Task<Result<MergeCategoriesResponse>> ExecuteAsync(
        MergeCategoriesRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.Equals(request.FromId, request.ToId, StringComparison.Ordinal))
            return Result<MergeCategoriesResponse>.Invalid(
                new ValidationError("toId", "Source and target categories must differ"));

        using var connection = await factory.OpenAsync(ct);

        var from = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(NameSql, new { Id = request.FromId }, cancellationToken: ct));
        if (from is null) return Result<MergeCategoriesResponse>.NotFound("Source category not found");

        var to = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(NameSql, new { Id = request.ToId }, cancellationToken: ct));
        if (to is null) return Result<MergeCategoriesResponse>.NotFound("Target category not found");

        // Same reason delete refuses it: the import pipeline assigns to
        // Uncategorised, so it has to keep existing. Merging *into* it is fine.
        if (string.Equals(from, DeleteCategoryCommand.Uncategorised, StringComparison.Ordinal))
            return Result<MergeCategoriesResponse>.Conflict(
                $"The {DeleteCategoryCommand.Uncategorised} category cannot be merged away");

        using var transaction = connection.BeginTransaction();

        var parameters = new { request.FromId, request.ToId, FromName = from, ToName = to };
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(MergeSql, parameters, transaction, cancellationToken: ct));

        var merged = await grid.ReadSingleAsync<int>();
        var rulesRepointed = await grid.ReadSingleAsync<int>();
        var conditionsRewritten = await grid.ReadSingleAsync<int>();

        transaction.Commit();

        return Result<MergeCategoriesResponse>.Success(new MergeCategoriesResponse
        {
            Merged = merged,
            RulesRepointed = rulesRepointed,
            ConditionsRewritten = conditionsRewritten,
            From = from,
            To = to,
        });
    }
}
