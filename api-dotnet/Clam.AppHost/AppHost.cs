var builder = DistributedApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────
//
// AddConnectionString, *not* AddSqlServer.
//
// AddSqlServer("clam") would start mcr.microsoft.com/mssql/server in a container
// and hand back its connection string. There is no Docker on this machine, and
// the database that matters locally is the LocalDB instance DataGrip and the
// dev API already point at — a container would be a second, empty database.
//
// AddConnectionString instead resolves ConnectionStrings:Clam from *this*
// project's configuration (appsettings.json, or user-secrets) and injects it
// into the API as the ConnectionStrings__Clam environment variable, which is
// exactly what Clam.Api's Program.cs already reads. Aspire manages the
// reference and shows the resource on the dashboard without owning its lifetime.
//
// Trusted_Connection works because the API runs as a local process. It would not
// if the API were containerised — that is the trap PremPoints' AppHost hit, and
// why it carries a hardcoded host.docker.internal string with sa credentials.
var clamDb = builder.AddConnectionString("Clam");

// ── .NET API ──────────────────────────────────────────────────────────────
var api = builder.AddProject<Projects.Clam_Api>("clam-api")
                 .WithReference(clamDb)
                 // Aspire holds the resource "unhealthy" until this returns 200,
                 // so the dashboard shows the API as starting rather than running
                 // while LocalDB wakes up. /alive is the shallow probe on
                 // purpose — gating startup on the database would make a cold
                 // Azure SQL look like a crash. /healthz is the deep one.
                 .WithHttpHealthCheck("/alive")
                 .WithExternalHttpEndpoints();

// ── React client ──────────────────────────────────────────────────────────
//
// The client is a workspace of the Bun monorepo at the repo root, so WithBun()
// rather than the npm default — otherwise `npm install` would fight bun.lock and
// the "workspace:*" reference to @clam/core would not resolve.
//
// No WithHttpEndpoint: AddViteApp assigns the port itself and passes it as PORT,
// which is why client/vite.config.ts reads process.env.PORT. Pinning a port here
// is a documented mistake.
//
// API_URL is the seam that already existed — vite.config.ts proxies /api to
// `process.env.API_URL ?? "http://localhost:3000"`. Setting it here points the
// client at the .NET API instead of the Express server, with no client change.
// Drop this line and the client falls back to Express on :3000.
builder.AddViteApp("clam-client", "../../client")
       .WithBun()
       .WithReference(api)
       .WaitFor(api)
       .WithEnvironment("API_URL", api.GetEndpoint("http"))
       .WithExternalHttpEndpoints();

builder.Build().Run();
