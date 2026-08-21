using Ardalis.Result;
using Clam.Api.Infrastructure.Auth;
using Clam.Api.Infrastructure.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Clam.Api.Features.Users.ProvisionCurrentUser;

/// Creates the local row for whoever is calling, linked to their WorkOS `sub`.
///
/// This exists because a WorkOS access token proves identity and carries no
/// profile: `sub` and nothing else. The name and email are on the ID token,
/// which the browser holds and this API never sees, so the client is the only
/// party that can supply them. It sends them here once, after its first sign-in.
///
/// **It cannot claim an existing row.** Matching an unlinked row by email would
/// make migration convenient and would also mean that anyone who can create a
/// WorkOS account with Alex's address becomes Alex. Rows that predate WorkOS get
/// their [workOsUserId] set by the data migration, under a DBA's eye, not by an
/// HTTP request.
public sealed class ProvisionCurrentUserCommand(
    IDbConnectionFactory factory,
    ICurrentUserAccessor currentUser,
    IIdGenerator ids)
{
    private const int UniqueConstraintViolation = 2627;
    private const int DuplicateKeyInIndex = 2601;

    private const string InsertSql = """
        INSERT INTO [Users] ([id], [workOsUserId], [name], [email], [owner])
        OUTPUT INSERTED.[id], INSERTED.[name], INSERTED.[email], INSERTED.[createdAt]
        VALUES (@Id, @WorkOsUserId, @Name, @Email, @Owner);
        """;

    public async Task<Result<UserSummary>> ExecuteAsync(ProvisionCurrentUserRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caller = currentUser.Get();
        if (caller is null) return Result<UserSummary>.Unauthorized();

        if (caller.IsProvisioned)
            return Result<UserSummary>.Conflict("This sign-in already has an account.");

        using var connection = await factory.OpenAsync(ct);

        try
        {
            var created = await connection.QuerySingleAsync<UserSummary>(new CommandDefinition(
                InsertSql,
                new
                {
                    Id = ids.NewId(),
                    WorkOsUserId = caller.WorkOsUserId,
                    request.Name,
                    request.Email,
                    request.Owner,
                },
                cancellationToken: ct));

            return Result<UserSummary>.Created(created);
        }
        catch (SqlException ex) when (ex.Number is UniqueConstraintViolation or DuplicateKeyInIndex)
        {
            // Either the address is taken, or this `sub` raced itself. Both mean
            // the same thing to the caller: there is already an account here and
            // it is not theirs to create.
            return Result<UserSummary>.Conflict("An account already exists for that email address.");
        }
    }
}
