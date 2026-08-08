using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace Clam.Api.Infrastructure.Security;

/// CORS, rate limiting and WorkOS token validation.
///
/// Ported from the PremPoints setup, with two deliberate changes noted at each
/// method. Nothing here knows what a Transaction is — it is host plumbing, which
/// is why it sits in Infrastructure rather than under Features.
public static class SecurityExtensions
{
    public const string CorsPolicy = "clam";
    public const string RateLimitPolicy = "DefaultPolicy";

    /// Origins come from configuration rather than being hardcoded, so the
    /// deployed environment does not need a code change. Development falls back
    /// to the Vite dev server, which is where it is called from in practice.
    public static IServiceCollection AddConfiguredCors(
        this IServiceCollection services,
        IConfiguration config,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(config);

        var allowedOrigins = config.GetSection("AllowedOrigins").Get<string[]>() ?? [];

        if (allowedOrigins.Length == 0 && isDevelopment)
        {
            allowedOrigins = ["http://localhost:5173", "http://127.0.0.1:5173"];
        }

        return services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            // Better Auth sends its session cookie cross-origin from the Vite dev
            // server. Requires an explicit origin list — this is why the policy
            // cannot use AllowAnyOrigin.
            .AllowCredentials()));
    }

    /// One difference from PremPoints worth knowing about: the limiter is
    /// registered globally rather than opted into per endpoint with
    /// `.RequireRateLimiting(...)`. Opt-in means a new endpoint is unprotected
    /// until someone remembers the attribute, and nothing fails to remind them.
    ///
    /// Partitioned by client IP so one caller cannot exhaust the window for
    /// everybody, which a single unpartitioned fixed window would allow.
    public static IServiceCollection AddDefaultRateLimiting(
        this IServiceCollection services,
        IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var permitLimit = config.GetValue("RateLimiting:PermitLimit", 120);
        var windowSeconds = config.GetValue("RateLimiting:WindowSeconds", 60);

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                    }));

            // Kept as a named policy too, so a specific endpoint can be given a
            // tighter budget later with .RequireRateLimiting(RateLimitPolicy).
            options.AddFixedWindowLimiter(RateLimitPolicy, opt =>
            {
                opt.PermitLimit = permitLimit;
                opt.Window = TimeSpan.FromSeconds(windowSeconds);
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                opt.QueueLimit = 0;
            });

            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = windowSeconds.ToString();
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    title = "Too Many Requests",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = $"Rate limit is {permitLimit} requests per {windowSeconds}s.",
                }, ct);
            };
        });
    }

    /// Validates AuthKit access tokens against the WorkOS JWKS.
    ///
    /// Registers nothing when WorkOS:ClientId is blank — which is the shipped
    /// state, so a fresh clone starts without credentials. Calling AddJwtBearer
    /// with an empty Authority would instead fail at the first request with a
    /// discovery error that looks like a network problem.
    ///
    /// The other difference from PremPoints: no OnTokenValidated hook. That one
    /// hits the database on every single request to map the WorkOS subject onto
    /// an internal user id. This schema has no User table, and adding a
    /// per-request query for a claim nothing reads yet would be pure cost.
    public static IServiceCollection AddWorkOsAuthentication(
        this IServiceCollection services,
        IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var clientId = config["WorkOS:ClientId"];

        if (string.IsNullOrWhiteSpace(clientId))
        {
            // AddAuthentication() with no scheme still has to be called.
            // UseAuthentication() resolves IAuthenticationSchemeProvider from the
            // container, and without this the *host fails to start* — which,
            // since blank is the shipped default, would mean a fresh clone or an
            // unconfigured deployment never boots at all.
            //
            // Registered but scheme-less is the correct end state here: the
            // pipeline is uniform, and there is simply no scheme that can
            // authenticate anyone.
            services.AddAuthentication();
            services.AddAuthorization();
            return services;
        }

        // Override for an AuthKit custom domain (https://<sub>.authkit.app),
        // whose JWKS lives at /oauth2/jwks instead. Default is the classic User
        // Management issuer, which is what PremPoints validates against today.
        var authority = config["WorkOS:Authority"]
            ?? $"https://api.workos.com/user_management/{clientId}";

        var audience = config["WorkOS:Audience"];

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    // WorkOS omits `aud` on classic User Management tokens, so
                    // validating it there rejects every token. AuthKit custom
                    // domains do set it to the client id — fill in
                    // WorkOS:Audience and it is checked.
                    ValidateAudience = !string.IsNullOrWhiteSpace(audience),
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();
        return services;
    }
}
