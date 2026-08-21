using System.Security.Claims;
using System.Threading.RateLimiting;
using Clam.Api.Infrastructure.Data;
using Dapper;
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

    /// Validates AuthKit access tokens against the WorkOS JWKS, and projects the
    /// caller's local user row onto the principal.
    ///
    /// **Whether this registers anything is the whole of the API's access
    /// control.** With no scheme registered, <see cref="AuthEnabled"/> is false
    /// and Program.cs leaves every endpoint anonymous — so a blank client id
    /// does not mean "unprotected endpoints return 401", it means the API is
    /// open. That is correct for the local host and the integration tests, which
    /// boot without credentials, and unacceptable anywhere else, so Production
    /// refuses to start rather than come up open. PremPoints throws
    /// unconditionally; it has no test host that needs the other behaviour.
    public static IServiceCollection AddWorkOsAuthentication(
        this IServiceCollection services,
        IConfiguration config,
        bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(config);

        var workOs = WorkOsOptions.TryFromConfiguration(config);

        if (workOs is null)
        {
            if (isProduction)
            {
                throw new InvalidOperationException(
                    $"{WorkOsOptions.SectionName}:ClientId is not configured. Without it no authentication " +
                    "scheme is registered and every endpoint would be reachable anonymously, so the host " +
                    "refuses to start in Production. Set WorkOS__ClientId in the deployment.");
            }

            // AddAuthentication() with no scheme still has to be called:
            // UseAuthentication() resolves IAuthenticationSchemeProvider from the
            // container, and without it the host fails to start at all.
            services.AddAuthentication();
            services.AddAuthorization();
            return services;
        }

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.Authority = workOs.Issuer;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = workOs.Issuer,
                    // WorkOS omits `aud` on classic User Management tokens, so
                    // validating it there rejects every token. AuthKit custom
                    // domains do set it to the client id — fill in
                    // WorkOS:Audience and it is checked.
                    ValidateAudience = workOs.Audience is not null,
                    ValidAudience = workOs.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = OnTokenValidatedAsync,
                };
            });

        services.AddAuthorization();
        return services;
    }

    /// True once a scheme has actually been registered. Program.cs reads this to
    /// decide whether endpoints require authorization — asking for it with no
    /// scheme registered throws on the first challenge rather than returning
    /// 401, so the two decisions have to agree.
    public static bool AuthEnabled(IConfiguration config) =>
        WorkOsOptions.TryFromConfiguration(config) is not null;

    /// A valid token proves *which WorkOS user* is calling and nothing else — an
    /// access token carries `sub` and no email or name at all, those being on the
    /// ID token. So the local row is the only source of anything this API needs
    /// about the caller, and it is looked up here and hung on the principal.
    ///
    /// A token with no matching row is left authenticated but unmapped, rather
    /// than rejected: that is the state a newly invited user is in before they
    /// provision, and `GET /me` answers 404 so the client knows to.
    ///
    /// No role claim, unlike PremPoints. There is no role column and no admin
    /// gate here — every signed-in user reaches every route, which is the whole
    /// of this app's authorization model and is documented as such.
    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var workOsUserId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.Principal?.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(workOsUserId)) return;
        if (context.Principal?.Identity is not ClaimsIdentity identity) return;

        var factory = context.HttpContext.RequestServices.GetRequiredService<IDbConnectionFactory>();
        using var connection = await factory.OpenAsync(context.HttpContext.RequestAborted);

        var user = await connection.QuerySingleOrDefaultAsync<LocalUser>(new CommandDefinition(
            "SELECT [id], [owner] FROM [Users] WHERE [workOsUserId] = @WorkOsUserId;",
            new { WorkOsUserId = workOsUserId },
            cancellationToken: context.HttpContext.RequestAborted));

        if (user is null) return;

        identity.AddClaim(new Claim(ClamClaims.UserId, user.Id));

        // Alex / Casey / Joint. Not an authorization boundary — it is which
        // person's figures a page defaults to, and every user may read every
        // owner's data.
        if (user.Owner is not null) identity.AddClaim(new Claim(ClamClaims.Owner, user.Owner));
    }

    private sealed class LocalUser
    {
        public string Id { get; set; } = "";
        public string? Owner { get; set; }
    }
}

/// Claim types this API adds to a validated principal. Constants rather than
/// literals: a typo in a claim name reads as "not signed in" rather than as an
/// error.
public static class ClamClaims
{
    public const string UserId = "clam:userId";
    public const string Owner = "clam:owner";
}
