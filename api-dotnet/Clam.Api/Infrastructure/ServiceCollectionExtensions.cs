using System.Text.Json;
using System.Text.Json.Serialization;
using Clam.Api.Features.Categories.GetCategories;
using Clam.Api.Features.Dashboard.GetDashboardSummary;
using Clam.Api.Features.Dev.SeedData;
using Clam.Api.Features.Transactions.GetTransactions;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Errors;
using Clam.Api.Infrastructure.Json;
using FastEndpoints;
using FastEndpoints.Swagger;

namespace Clam.Api.Infrastructure;

/// Registration, grouped by what it is for, so Program.cs stays a readable list
/// of decisions rather than a wall of `services.Add*`.
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiInfrastructure(this IServiceCollection services)
    {
        services.AddFastEndpoints();
        services.SwaggerDocument(o => o.DocumentSettings = s =>
        {
            s.Title = "Clam Finance Tracker API";
            s.Version = "v1";
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        return services;
    }

    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IDbConnectionFactory>(new SqlConnectionFactory(connectionString));

        // The deep readiness probe behind /healthz. Tagged so it is excluded from
        // /alive, which must never touch the database — see Clam.ServiceDefaults.
        services.AddHealthChecks()
            .AddSqlServer(
                connectionString: connectionString,
                healthQuery: "SELECT 1;",
                name: "sql",
                timeout: TimeSpan.FromSeconds(5),
                tags: ["ready", "db"]);

        return services;
    }

    /// One registration per slice. Explicit rather than assembly-scanned: the
    /// list is short, and a slice that forgets to appear here fails at startup
    /// instead of on the first request.
    public static IServiceCollection AddFeatureSlices(this IServiceCollection services)
    {
        services.AddScoped<GetTransactionsQuery>();
        services.AddScoped<GetCategoriesQuery>();
        services.AddScoped<GetDashboardSummaryQuery>();
        services.AddScoped<SeedDataCommand>();

        return services;
    }

    /// Everything about the wire format lives here.
    ///
    /// It is dictated by the existing Express API, not by C# defaults — see the
    /// converters for why each one is present. Property naming is left at the
    /// FastEndpoints default (camelCase), which already matches Prisma.
    public static void ConfigureSerializer(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Converters.Add(new DecimalAsStringConverter());
        options.Converters.Add(new UtcDateTimeConverter());
        options.Converters.Add(new JsonStringEnumConverter());
    }
}
