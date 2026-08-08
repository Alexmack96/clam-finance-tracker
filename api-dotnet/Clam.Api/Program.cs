using Clam.Api.Features.Dev.SeedData;
using Clam.Api.Infrastructure;
using Clam.Api.Infrastructure.Security;
using Clam.ServiceDefaults;
using FastEndpoints;
using FastEndpoints.Swagger;

var builder = WebApplication.CreateBuilder(args);

// Supplied by the Aspire AppHost as ConnectionStrings__Clam when running under
// it, and by appsettings.Development.json when this project is launched alone.
var connectionString = builder.Configuration.GetConnectionString("Clam")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Clam is not configured. Set it in appsettings.Development.json " +
        "for LocalDB, or as the ConnectionStrings__Clam environment variable in a deployment.");

var seedEnabled = builder.Configuration.GetValue("Seed:Enabled", builder.Environment.IsDevelopment());

builder.AddServiceDefaults();   // OpenTelemetry, service discovery, liveness check

builder.Services
    .AddApiInfrastructure()           // FastEndpoints, Swagger, ProblemDetails
    .AddPersistence(connectionString) // Dapper connection factory, SQL health check
    .AddFeatureSlices()               // one registration per vertical slice
    .AddConfiguredCors(builder.Configuration, builder.Environment.IsDevelopment())
    .AddDefaultRateLimiting(builder.Configuration)
    .AddWorkOsAuthentication(builder.Configuration);

var app = builder.Build();

// ── Pipeline. The order below is the behaviour; do not shuffle it. ─────────

// First, so it catches whatever anything after it throws.
app.UseExceptionHandler();

// Before auth: a rejected preflight can never carry credentials, so the browser
// reports a CORS error instead of the 401 that actually happened.
app.UseCors(SecurityExtensions.CorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

// After authentication, so the limiter could be partitioned per user rather than
// per IP once there is a user to partition by.
app.UseRateLimiter();

app.UseFastEndpoints(c =>
{
    // Every endpoint declares its route without the prefix, so routes read
    // "transactions" and the prefix lives in exactly one place.
    c.Endpoints.RoutePrefix = "api";

    // FastEndpoints discovers endpoints by reflection, so keeping the seeder out
    // of a deployment is a filter rather than an `if` around a registration.
    c.Endpoints.Filter = ep => seedEnabled || ep.EndpointType != typeof(SeedDataEndpoint);

    ServiceCollectionExtensions.ConfigureSerializer(c.Serializer.Options);

    // Validation failures come back as RFC 7807 too, so a 400 from a validator
    // and a 409 from a Result are the same shape to the client.
    c.Errors.UseProblemDetails();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerGen();
}

// /alive (liveness) and /healthz (per-dependency JSON). Deliberately outside the
// "api" prefix: they are host concerns, not part of the API surface the client
// consumes, and the Aspire dashboard probes /alive by absolute path.
app.MapDefaultEndpoints();

app.Run();

/// Exposed so the integration test project can boot the real pipeline through
/// FastEndpoints' AppFixture. Top-level statements generate an internal Program
/// class, which a separate assembly cannot name.
public partial class Program;
