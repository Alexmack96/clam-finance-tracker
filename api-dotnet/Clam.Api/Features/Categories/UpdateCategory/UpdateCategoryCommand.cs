using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Clam.Api.Features.Categories.UpdateCategory;

public sealed class UpdateCategoryCommand(IDbConnectionFactory factory)
{
    /// COALESCE is how "absent means leave it alone" is expressed without
    /// building the SET list at runtime — one statement, one cached plan.
    private const string UpdateSql = """
        UPDATE  [Categories]
        SET     [name]  = COALESCE(@Name, [name]),
                [color] = COALESCE(@Color, [color])
        OUTPUT  INSERTED.[id], INSERTED.[name], INSERTED.[color]
        WHERE   [id] = @Id;
        """;

    private const string SelectNameSql = "SELECT [name] FROM [Categories] WHERE [id] = @Id;";

    /// Bucket rules match on category *name* so that Contains/StartsWith stay
    /// meaningful ("category contains Sauce"). A rename would silently break
    /// them, so exact-match conditions are rewritten to follow. Partial-match
    /// conditions are deliberately left alone — the server cannot know whether
    /// "contains Sauce" was aimed at this category or a family of them.
    private const string RenameConditionsSql = """
        UPDATE  [RuleConditions]
        SET     [value] = @NewName
        WHERE   [field] = 'Category' AND [operator] = 'Exact' AND [value] = @OldName;
        """;

    public async Task<Result<UpdateCategoryResponse>> ExecuteAsync(
        UpdateCategoryRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = request.Name?.Trim();

        using var connection = await factory.OpenAsync(ct);

        var before = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(SelectNameSql, new { request.Id }, cancellationToken: ct));

        if (before is null) return Result<UpdateCategoryResponse>.NotFound("Category not found");

        UpdateCategoryResponse? updated;
        try
        {
            updated = await connection.QuerySingleOrDefaultAsync<UpdateCategoryResponse>(
                new CommandDefinition(UpdateSql, new { request.Id, Name = name, request.Color }, cancellationToken: ct));
        }
        catch (SqlException ex) when (CategoryErrors.IsDuplicateName(ex))
        {
            return Result<UpdateCategoryResponse>.Conflict(CategoryErrors.DuplicateNameMessage(name ?? before));
        }

        // Deleted between the two statements. Rare, but the alternative is
        // returning a 200 describing a row that no longer exists.
        if (updated is null) return Result<UpdateCategoryResponse>.NotFound("Category not found");

        if (name is not null && !string.Equals(name, before, StringComparison.Ordinal))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                RenameConditionsSql, new { OldName = before, NewName = name }, cancellationToken: ct));
        }

        return Result<UpdateCategoryResponse>.Success(updated);
    }
}
