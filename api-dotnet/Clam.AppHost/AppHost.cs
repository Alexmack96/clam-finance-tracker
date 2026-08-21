var builder = DistributedApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────
//
// AddConnectionString, *not* AddSqlServer.
//
// AddSqlServer("clam") would start mcr.microsoft.com/mssql/server in a container
// and hand back its connection string. There is no Docker on this machine, and
// the database that matters is Azure SQL — a container would be a second, empty
// one that nothing else points at.
//
// AddConnectionString instead resolves ConnectionStrings:Clam from *this*
// project's configuration and injects it into the API as the
// ConnectionStrings__Clam environment variable, which is exactly what
// Clam.Api's Program.cs already reads. Aspire manages the reference and shows
// the resource on the dashboard without owning its lifetime.
//
// **It comes from user-secrets on this project, and nowhere else.** There is no
// value in appsettings.json, on purpose: the string carries an Azure SQL
// password, and a committed LocalDB fallback would silently take over whenever
// the secret was missing. Note the precedence too — this environment variable
// beats Clam.Api's *own* user-secrets, so setting the string only on Clam.Api
// works when the API runs alone and is quietly ignored under Aspire.
//
// LocalDB is for the integration tests. They build a throwaway database per run
// and never read this.
var clamDb = builder.AddConnectionString("Clam");

// ── .NET API ──────────────────────────────────────────────────────────────
var api = builder.AddProject<Projects.Clam_Api>("clam-api")
                 .WithReference(clamDb)
                 // Pinned rather than left to Aspire's dynamic allocation, so
                 // http://localhost:5299/api/categories stays bookmarkable, the
                 // .http file keeps working, and the address matches what the
                 // project uses when launched on its own.
                 .WithEndpoint("http", e => e.Port = 5299)
                 // Adds a "Swagger" link alongside the endpoint on the dashboard,
                 // so the API reference is one click from the resource list
                 // rather than a path you have to remember.
                 //
                 // The callback overload that returns a new annotation *adds* a
                 // link. The `url => { ... }` overload mutates the endpoint's own
                 // URL instead, which would replace the plain endpoint link
                 // rather than sit next to it.
                 .WithUrlForEndpoint("http", _ => new ResourceUrlAnnotation
                 {
                     Url = "/swagger",
                     DisplayText = "Swagger",
                 })
                 // Aspire holds the resource "unhealthy" until this returns 200,
                 // so the dashboard shows the API as starting rather than running
                 // while a paused Azure SQL serverless database resumes. /alive
                 // is the shallow probe on purpose — gating startup on the
                 // database would make that resume look like a crash. /healthz
                 // is the deep one.
                 .WithHttpHealthCheck("/alive")
                 .WithExternalHttpEndpoints();

// ── React client ──────────────────────────────────────────────────────────
//
// The client is a workspace of the Bun monorepo at the repo root, so WithBun()
// rather than the npm default — otherwise `npm install` would fight bun.lock and
// the "workspace:*" reference to @clam/core would not resolve.
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
       // Vite on a fixed 5173, with no Aspire proxy in front of it. WorkOS is
       // why it is fixed, and this project's own predev is why there is no proxy.
       //
       // AddViteApp leaves the port dynamic, which is fine until something
       // off-machine has to know the address. AuthKit defaults its redirect_uri
       // to window.origin and WorkOS matches redirect URIs exactly against an
       // allowlist maintained by hand, so a port that changes every run can never
       // be on it. That failure does not look like a bad port either: WorkOS
       // falls back to whichever redirect URI *is* registered in the environment
       // and sends you to a different app entirely.
       //
       // IsProxied = false is the load-bearing half. Pinning only the proxy port
       // puts an Aspire listener on 5173, and the client's own `predev`
       // (scripts/free-ports.ts, run by bun before vite) force-kills whatever
       // holds 5173 — so the resource shot its own front door on every start and
       // took the AppHost down with it. Unproxied, Vite binds 5173 itself, and
       // free-ports goes back to doing what it was written for: clearing a stale
       // vite from a previous session.
       //
       // Mutating the existing endpoint rather than adding one: WithHttpEndpoint
       // here would create a second endpoint instead of changing this one.
       .WithEndpoint("http", e =>
       {
           e.Port = 5173;
           e.TargetPort = 5173;
           e.IsProxied = false;
       })
       .WithExternalHttpEndpoints();

builder.Build().Run();
