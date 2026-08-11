using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Clam.Api.Features.Categories.CreateCategory;

public sealed class CreateCategoryCommand(IDbConnectionFactory factory, IIdGenerator ids)
{
    private const string Sql = """
        INSERT INTO [Categories] ([id], [name], [color])
        VALUES (@Id, @Name, @Color);
        """;

    public async Task<Result<CreateCategoryResponse>> ExecuteAsync(
        CreateCategoryRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Trimmed here rather than only in the validator, so the name that is
        // stored is the name that was checked for length.
        var category = new CreateCategoryResponse
        {
            Id = ids.NewId(),
            Name = request.Name.Trim(),
            Color = request.Color,
        };

        using var connection = await factory.OpenAsync(ct);

        // Insert first and catch the collision rather than checking for an
        // existing name first: the check-then-act version is a race, and the
        // unique index has to be honoured either way.
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(Sql, category, cancellationToken: ct));
        }
        catch (SqlException ex) when (CategoryErrors.IsDuplicateName(ex))
        {
            return Result<CreateCategoryResponse>.Conflict(CategoryErrors.DuplicateNameMessage(category.Name));
        }

        return Result<CreateCategoryResponse>.Created(category);
    }
}
