using Ardalis.Result;
using Clam.Api.Domain;
using Clam.Api.Infrastructure.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Clam.Api.Features.Investments.CreateInvestmentAccount;

public sealed class CreateInvestmentAccountCommand(IDbConnectionFactory factory, IIdGenerator ids)
{
    private const int UniqueConstraintViolation = 2627;
    private const int DuplicateKeyInIndex = 2601;

    /// The sort order defaults to one past this owner's current maximum, read in
    /// the same statement so two accounts created at once cannot collide.
    private const string Sql = $"""
        INSERT INTO [InvestmentAccounts] ([id], [name], [category], [owner], [rate], [sortOrder])
        OUTPUT {InvestmentSql.AccountInserted}
        SELECT  @Id, @Name, @Category, @Owner, @Rate,
                COALESCE(@SortOrder, (SELECT COALESCE(MAX([sortOrder]), 0) + 1
                                      FROM [InvestmentAccounts] WHERE [owner] = @Owner));
        """;

    public async Task<Result<CreateInvestmentAccountResponse>> ExecuteAsync(
        CreateInvestmentAccountRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = new
        {
            Id = ids.NewId(),
            request.Name,
            request.Category,
            Owner = (request.Owner ?? Domain.Owner.Alex).ToString(),
            request.Rate,
            request.SortOrder,
        };

        using var connection = await factory.OpenAsync(ct);

        try
        {
            var created = await connection.QuerySingleAsync<CreateInvestmentAccountResponse>(
                new CommandDefinition(Sql, parameters, cancellationToken: ct));

            return Result<CreateInvestmentAccountResponse>.Created(created);
        }
        catch (SqlException ex) when (ex.Number is UniqueConstraintViolation or DuplicateKeyInIndex)
        {
            // (owner, name) is unique: two "Vanguard" accounts for the same
            // person would be indistinguishable in every chart.
            return Result<CreateInvestmentAccountResponse>.Conflict(
                $"{parameters.Owner} already has an account named \"{request.Name}\"");
        }
    }
}
