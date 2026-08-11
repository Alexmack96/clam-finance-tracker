using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Categories.DeleteCategory;

public sealed class DeleteCategoryCommand(IDbConnectionFactory factory)
{
    /// The name and the usage count in one round trip: both are needed to decide
    /// whether the delete is allowed at all.
    private const string InspectSql = """
        SELECT  c.[name],
                (SELECT COUNT(*) FROM [Transactions] t WHERE t.[categoryId] = c.[id]) AS [InUse]
        FROM    [Categories] c
        WHERE   c.[id] = @Id;
        """;

    private const string DeleteSql = "DELETE FROM [Categories] WHERE [id] = @Id;";

    /// The fallback the import pipeline assigns to. Deleting it would break
    /// processing, so it can never be removed.
    internal const string Uncategorised = "Uncategorised";

    public async Task<Result> ExecuteAsync(DeleteCategoryRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);

        var category = await connection.QuerySingleOrDefaultAsync<CategoryUsage>(
            new CommandDefinition(InspectSql, new { request.Id }, cancellationToken: ct));

        if (category is null) return Result.NotFound("Category not found");

        if (string.Equals(category.Name, Uncategorised, StringComparison.Ordinal))
            return Result.Conflict($"The {Uncategorised} category cannot be deleted");

        // A category in use cannot be deleted without orphaning transactions —
        // point the user at merge, which reassigns them first.
        if (category.InUse > 0)
        {
            var plural = category.InUse == 1 ? "" : "s";
            return Result.Conflict(
                $"\"{category.Name}\" still has {category.InUse} transaction{plural}. Merge it into another category first.");
        }

        await connection.ExecuteAsync(new CommandDefinition(DeleteSql, new { request.Id }, cancellationToken: ct));
        return Result.NoContent();
    }

    private sealed class CategoryUsage
    {
        public string Name { get; set; } = "";
        public int InUse { get; set; }
    }
}
