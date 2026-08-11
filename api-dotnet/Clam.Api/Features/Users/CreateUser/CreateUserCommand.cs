using Ardalis.Result;
using Clam.Api.Infrastructure.Auth;
using Clam.Api.Infrastructure.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Clam.Api.Features.Users.CreateUser;

/// Sign-up is disabled in Better Auth, so this is the only way an account comes
/// into existence. It writes both halves — the user and the credential row
/// Better Auth looks the password up in — because a user without one can never
/// sign in and looks identical to one who can.
public sealed class CreateUserCommand(IDbConnectionFactory factory, IIdGenerator ids)
{
    private const int UniqueConstraintViolation = 2627;
    private const int DuplicateKeyInIndex = 2601;

    private const string InsertUserSql = """
        INSERT INTO [Users] ([id], [name], [email])
        OUTPUT INSERTED.[id], INSERTED.[name], INSERTED.[email], INSERTED.[createdAt]
        VALUES (@Id, @Name, @Email);
        """;

    /// `providerId = 'credential'` is Better Auth's marker for a password login
    /// as opposed to an OAuth link, and `accountId` mirrors the user id for that
    /// provider. Both are its schema, not a choice made here.
    private const string InsertCredentialSql = """
        INSERT INTO [Accounts] ([id], [accountId], [providerId], [userId], [password])
        VALUES (@Id, @UserId, 'credential', @UserId, @Password);
        """;

    public async Task<Result<UserSummary>> ExecuteAsync(CreateUserRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Hashed before the transaction opens: scrypt at Better Auth's cost
        // parameters takes tens of milliseconds, and that is not time to hold a
        // write transaction over the Users table.
        var hash = BetterAuthPasswordHasher.Hash(request.Password);
        var userId = ids.NewId();

        using var connection = await factory.OpenAsync(ct);
        using var transaction = connection.BeginTransaction();

        UserSummary created;
        try
        {
            created = await connection.QuerySingleAsync<UserSummary>(new CommandDefinition(
                InsertUserSql,
                new { Id = userId, request.Name, request.Email },
                transaction,
                cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number is UniqueConstraintViolation or DuplicateKeyInIndex)
        {
            return Result<UserSummary>.Conflict("Email already in use");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            InsertCredentialSql,
            new { Id = ids.NewId(), UserId = userId, Password = hash },
            transaction,
            cancellationToken: ct));

        transaction.Commit();
        return Result<UserSummary>.Created(created);
    }
}
