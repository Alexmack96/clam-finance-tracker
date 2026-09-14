using Clam.Api.Features.Categories;
using Clam.Api.Features.Dev.SeedData;
using Clam.Api.Infrastructure;
using Clam.Api.Infrastructure.Security;
using Clam.ServiceDefaults;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Supplied by the Aspire AppHost as ConnectionStrings__ClamFinance when running
// under it, and by this project's user-secrets when it is launched alone. Never
// from appsettings: it carries an Azure SQL password.
var connectionString = builder.Configuration.GetConnectionString("ClamFinance");

// Blank, not just null. `??` alone lets an empty ConnectionStrings__ClamFinance
// through — which is exactly what a deployment sets when a secret fails to
// resolve — and the failure then surfaces much later as an unhelpful
// SqlException about a missing server name.
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:ClamFinance is not configured. It is not committed anywhere, because it " +
        "carries an Azure SQL password. Set it in user-secrets:\n" +
        "    dotnet user-secrets set \"ConnectionStrings:ClamFinance\" \"Server=tcp:...;\" --project Clam.Api\n" +
        "or, to run the whole stack, on Clam.AppHost instead — under Aspire the AppHost injects " +
        "ConnectionStrings__ClamFinance and that wins. In a deployment, set the environment variable " +
        "directly. LocalDB is for the integration tests, which build their own database per run.");
}

var seedEnabled = builder.Configuration.GetValue("Seed:Enabled", builder.Environment.IsDevelopment());

builder.AddServiceDefaults();   // OpenTelemetry, service discovery, liveness check

builder.Services
    .AddApiInfrastructure()           // FastEndpoints, Swagger, ProblemDetails
    .AddPersistence(connectionString) // Dapper connection factory, SQL health check
    .AddDatabaseKeepAlive(builder.Configuration) // timed ping, dark behind a feature flag
    .AddStatementStorage(builder.Configuration, builder.Environment)
    .AddExternalClients(builder.Configuration)  // Monzo and Frankfurter typed clients
    .AddFeatureSlices()               // one registration per vertical slice
    .AddConfiguredCors(builder.Configuration, builder.Environment.IsDevelopment())
    .AddDefaultRateLimiting(builder.Configuration)
    .AddWorkOsAuthentication(builder.Configuration, builder.Environment.IsProduction());

// The system categories and their starting bucket rules, brought up to date on
// every boot. On by default: a database without them imports everything as
// Uncategorised and reports an empty Needs/Wants/Savings split, which looks like
// a working app with no data rather than like a missing step. The tests turn it
// off — a background task inserting rows is not something to race Respawn with.
if (builder.Configuration.GetValue("SystemCategories:Seed", true))
{
    builder.Services.AddHostedService<SystemCategorySeeder>();
}

var authEnabled = SecurityExtensions.AuthEnabled(builder.Configuration);

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

    // FastEndpoints secures every endpoint unless it says AllowAnonymous(), so
    // requiring a token is the default and needs nothing here. Three endpoints
    // opt out, each saying why in its own Configure().
    //
    // What this does is the reverse: with no authentication scheme registered,
    // it opens everything. Asking for authorization when nothing can
    // authenticate does not produce 401s, it throws on the first challenge, so
    // the alternative is a local host and a test suite that 500 on every
    // request. AddWorkOsAuthentication is what keeps that state out of
    // Production, where it refuses to boot instead.
    if (!authEnabled)
    {
        c.Endpoints.Configurator = ep => ep.AllowAnonymous();
    }

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

// ── The React client, served by this process in a deployment ───────────────
//
// The Express service did this too, and it is the reason that service cannot
// simply be deleted once its routes are ported: it is also the web server. A
// single origin for the app and its API is also what keeps CORS out of
// production entirely.
//
// Off in Development, where Vite serves the client on :5173 with hot reload and
// proxies /api here. The directory is empty then anyway.
//
// **The /api fallback below is load-bearing.** A file fallback answers *any*
// unmatched request, and middleware order has nothing to do with it: both are
// endpoints, matched by route precedence rather than by registration order. So
// without it a mistyped /api/... comes back as index.html with a 200 — which
// reaches the client as "Unexpected token '<'" instead of as a missing route,
// and was exactly what this did until it was probed.
var clientRoot = builder.Configuration["ClientFiles:Root"]
    ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");

if (!app.Environment.IsDevelopment() && Directory.Exists(clientRoot))
{
    var clientFiles = new PhysicalFileProvider(Path.GetFullPath(clientRoot));

    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = clientFiles });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = clientFiles });

    // The SPA fallback. Client-side routes like /transactions exist only in the
    // browser's router, so a reload or a pasted link has to be answered with the
    // shell rather than a 404.
    app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = clientFiles });

    // …but never for the API. A literal segment outranks a catch-all, so this
    // fallback wins over the file one for anything under /api and an unknown
    // route answers 404 as it did before the client was served from here.
    app.MapFallback("/api/{**rest}", () => Results.NotFound());
}
else if (!app.Environment.IsDevelopment())
{
    // Said once, loudly, at boot. The alternative is every page load answering
    // 404 with nothing anywhere explaining why.
    app.Logger.LogWarning(
        "No client build found at {ClientRoot} — the API will serve its routes but nothing will serve the app. " +
        "Set ClientFiles:Root, or build the client into wwwroot.", clientRoot);
}

app.Run();

/// Exposed so the integration test project can boot the real pipeline through
/// FastEndpoints' AppFixture. Top-level statements generate an internal Program
/// class, which a separate assembly cannot name.
public partial class Program;
