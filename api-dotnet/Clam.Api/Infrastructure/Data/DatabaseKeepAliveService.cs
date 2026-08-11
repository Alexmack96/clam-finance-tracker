using Dapper;
using Microsoft.FeatureManagement;

namespace Clam.Api.Infrastructure.Data;

/// Pings the database on a timer so an auto-paused Azure SQL serverless database
/// never gets idle enough to pause, and the first real request of the day does
/// not pay the resume cost.
///
/// Off unless <c>FeatureManagement:DatabaseKeepAlive</c> is true. The flag is
/// read on every tick rather than once at startup, so it can be switched on
/// against a running instance — that is the whole reason it is a flag and not a
/// commented-out registration.
///
/// Scoping note: unlike the EF version of this pattern, there is no
/// IServiceScopeFactory here. <see cref="IDbConnectionFactory"/> is a singleton
/// that hands out a connection per call, so a hosted singleton can use it
/// directly; there is no scoped DbContext to resolve.
public sealed class DatabaseKeepAliveService(
    IDbConnectionFactory connections,
    IFeatureManager features,
    TimeProvider clock,
    ILogger<DatabaseKeepAliveService> logger) : BackgroundService
{
    /// The flag's name in configuration. Also referenced by the tests.
    public const string FeatureName = "DatabaseKeepAlive";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(45);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't ping on startup: something just connected, by definition.
        await Task.Delay(Interval, clock, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (await features.IsEnabledAsync(FeatureName))
            {
                await PingAsync(stoppingToken);
            }

            await Task.Delay(Interval, clock, stoppingToken);
        }
    }

    private async Task PingAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var connection = await connections.OpenAsync(stoppingToken);
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT 1;", cancellationToken: stoppingToken));
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // Swallowed on purpose. A failed keep-alive is not a failed request —
            // letting it escape would take the host down over a database that is
            // merely resuming, which is the exact condition this exists for.
            logger.LogWarning(ex, "Database keep-alive failed");
        }
    }
}
