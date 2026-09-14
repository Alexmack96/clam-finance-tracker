using Clam.Api.Infrastructure.Statements;
using Microsoft.Extensions.DependencyInjection;

namespace Clam.Api.Tests;

/// The suite's statement store must write into the throwaway directory Arrange
/// empties, never into Clam.Api/statements. That was where it wrote for as long
/// as the override sat in a configuration source that loses to the app's own,
/// and nothing failed: the tests passed while leaving PDFs in the folder local
/// development keeps real statements in.
public class StatementStorageLocationTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public void Uploads_are_written_to_the_suites_own_directory()
    {
        var store = Api.Services.GetRequiredService<IStatementStore>();

        var path = store.PathFor("probe.pdf");

        Assert.StartsWith(Path.GetFullPath(Arrange.StatementsDirectory), path, StringComparison.OrdinalIgnoreCase);
    }
}
