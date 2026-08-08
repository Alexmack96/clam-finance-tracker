using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Clam.ServiceDefaults;

/// Cross-cutting host wiring shared by every service in the solution: telemetry,
/// service discovery, resilient HTTP, and the health endpoints.
///
/// This is a separate project rather than a folder for one reason only — Aspire
/// requires it. `IsAspireSharedProject` is what lets the AppHost hand the same
/// defaults to a second service later without the API becoming its dependency.
/// It is not the place to put anything that knows what a Transaction is.
public static class Extensions
{
    /// Liveness: is the process up and serving? Never touches a dependency.
    public const string AlivenessEndpointPath = "/alive";

    /// Readiness: the detailed, per-dependency report. Rendered by the
    /// HealthChecks UI writer so the response is a JSON object with an entry per
    /// registered check, not the framework default of the bare string "Healthy".
    public const string HealthEndpointPath = "/healthz";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    tracing.SetSampler<AlwaysOnSampler>();
                }

                tracing
                    .AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(o =>
                        // The health endpoints are polled every few seconds by the
                        // Aspire dashboard. Tracing them buries every real request.
                        o.Filter = ctx =>
                            !ctx.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !ctx.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                    .AddHttpClientInstrumentation()
                    // This is what makes each Dapper query a child span on the
                    // dashboard's trace timeline — the payoff for hand-writing SQL
                    // instead of using an ORM with its own diagnostics.
                    //
                    // The SQL text itself rides along as db.query.text without
                    // being asked for: the old SetDbStatementForText opt-in was
                    // removed in 1.16 when the database semantic conventions went
                    // stable. SetDbQueryParameters is the one that is still
                    // opt-in, and it stays off — parameter values are the part
                    // that would put real amounts and descriptions in traces.
                    .AddSqlClientInstrumentation(o => o.RecordException = true);
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        // Set by the AppHost when running under Aspire; absent when the API is
        // launched on its own, in which case telemetry stays in-process.
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Both are exposed in every environment on purpose, and both are safe to
        // be: /alive returns no detail at all, and /healthz reports check names
        // and durations, never connection strings or exception text. The Azure
        // SQL database is not reachable from the internet either way.
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
        });

        app.MapHealthChecks(HealthEndpointPath, new HealthCheckOptions
        {
            // Everything registered, including the dependency checks.
            Predicate = _ => true,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        });

        return app;
    }
}
