using Microsoft.Data.SqlClient;

namespace Clam.Api.Features.Categories;

/// Feature-shared because three slices need it — create, update and merge all
/// have to tell a duplicate name apart from a genuine failure, and the way you
/// do that is a SQL Server error number rather than anything domain-shaped.
///
/// This is the .NET spelling of the `P2002` checks the Express routes make
/// against Prisma.
internal static class CategoryErrors
{
    private const int UniqueConstraintViolation = 2627;
    private const int DuplicateKeyInIndex = 2601;

    internal static bool IsDuplicateName(SqlException ex) =>
        ex.Number is UniqueConstraintViolation or DuplicateKeyInIndex;

    internal static string DuplicateNameMessage(string name) =>
        $"A category named \"{name}\" already exists";
}
