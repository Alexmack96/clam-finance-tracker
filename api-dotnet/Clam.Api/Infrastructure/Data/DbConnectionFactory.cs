using System.Data;
using Microsoft.Data.SqlClient;

namespace Clam.Api.Infrastructure.Data;

public interface IDbConnectionFactory
{
    Task<IDbConnection> OpenAsync(CancellationToken ct = default);
}

/// Hands out a freshly opened connection per call. Dapper does not pool or track
/// anything itself, so callers own the lifetime — always dispose.
///
/// Pooling is left to SqlClient, which does it per connection string. That is
/// also why the Azure connection string carries ConnectRetryCount: an auto-paused
/// serverless database refuses the first connection while it resumes, and this is
/// the cheapest place to absorb that without taking a Polly dependency.
public sealed class SqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public async Task<IDbConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
