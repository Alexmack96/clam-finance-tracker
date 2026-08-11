using System.Text.Json;
using System.Text.Json.Serialization;
using Clam.Api.Features.Categories.CreateCategory;
using Clam.Api.Features.Categories.DeleteCategory;
using Clam.Api.Features.Categories.GetCategories;
using Clam.Api.Features.Categories.MergeCategories;
using Clam.Api.Features.Categories.UpdateCategory;
using Clam.Api.Features.Dashboard.GetDashboardAnalytics;
using Clam.Api.Features.Dashboard.GetDashboardSummary;
using Clam.Api.Features.Dev.SeedData;
using Clam.Api.Features.Import.BackfillUsdGbp;
using Clam.Api.Features.Import.GetLastStatement;
using Clam.Api.Features.Import.GetStagedCounts;
using Clam.Api.Features.Import.ProcessStaged;
using Clam.Api.Features.Investments.CreateInvestmentAccount;
using Clam.Api.Features.Investments.DeleteInvestmentAccount;
using Clam.Api.Features.Investments.DeleteInvestmentSnapshot;
using Clam.Api.Features.Investments.DeleteInvestmentSnapshotsByDate;
using Clam.Api.Features.Investments.GetInvestments;
using Clam.Api.Features.Investments.UpdateInvestmentAccount;
using Clam.Api.Features.Investments.UpsertInvestmentSnapshot;
using Clam.Api.Features.Monzo;
using Clam.Api.Features.Monzo.GetMonzoRecRuns;
using Clam.Api.Features.Monzo.GetMonzoStatus;
using Clam.Api.Features.Monzo.RunMonzoRec;
using Clam.Api.Features.Monzo.SyncMonzo;
using Clam.Api.Features.Notes.CreateNote;
using Clam.Api.Features.Notes.DeleteNote;
using Clam.Api.Features.Notes.GetNotes;
using Clam.Api.Features.Notes.UpdateNote;
using Clam.Api.Features.Recurring.GetRecurring;
using Clam.Api.Features.Recurring.SetRecurringVerdict;
using Clam.Api.Features.Recurring.UpdateRecurringNote;
using Clam.Api.Features.Rules.ApplyRules;
using Clam.Api.Features.Rules.CreateRule;
using Clam.Api.Features.Rules.DeleteRule;
using Clam.Api.Features.Rules.GetRules;
using Clam.Api.Features.Rules.PreviewRules;
using Clam.Api.Features.Rules.ReorderRules;
using Clam.Api.Features.Rules.UpdateRule;
using Clam.Api.Features.Statements.DeleteStatement;
using Clam.Api.Features.Statements.DownloadStatement;
using Clam.Api.Features.Statements.GetStatement;
using Clam.Api.Features.Statements.GetStatements;
using Clam.Api.Features.Tabs.CreateTab;
using Clam.Api.Features.Tabs.DeleteTab;
using Clam.Api.Features.Tabs.GetTabs;
using Clam.Api.Features.Tabs.UpdateTab;
using Clam.Api.Features.Transactions.DeleteTransaction;
using Clam.Api.Features.Transactions.GetTransactions;
using Clam.Api.Features.Transactions.UpdateTransaction;
using Clam.Api.Features.Users.CreateUser;
using Clam.Api.Features.Users.GetUsers;
using Clam.Api.Features.Utilities.GetUtilities;
using Clam.Api.Infrastructure.Auth;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Errors;
using Clam.Api.Infrastructure.Fx;
using Clam.Api.Infrastructure.Json;
using Clam.Api.Infrastructure.Monzo;
using Clam.Api.Infrastructure.Statements;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.FeatureManagement;
using FastEndpoints;
using FastEndpoints.Swagger;

namespace Clam.Api.Infrastructure;

/// Registration, grouped by what it is for, so Program.cs stays a readable list
/// of decisions rather than a wall of `services.Add*`.
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiInfrastructure(this IServiceCollection services)
    {
        // The clock is a dependency like any other. Nothing in this service reads
        // DateTime.UtcNow directly — a test that cannot choose the instant cannot
        // assert on anything derived from it, and half of this API's responses are.
        services.AddSingleton(TimeProvider.System);

        // Ids are minted from the clock, so the generator is injected for the
        // same reason rather than being a static helper.
        services.AddSingleton<IIdGenerator, CuidGenerator>();

        services.AddFastEndpoints();
        services.SwaggerDocument(o => o.DocumentSettings = s =>
        {
            s.Title = "Clam Finance Tracker API";
            s.Version = "v1";
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        // Kestrel's default is 30 MB, which is a sensible ceiling for a service
        // that accepts uploads and an absurd one for this API — every endpoint
        // here takes a small JSON document, and the largest single field is a
        // note. A `MaximumLength` cannot help: the body is read and deserialised
        // before any validator sees it, so the only bound on a 30 MB request full
        // of one field is this one.
        //
        // Deliberately generous against that yardstick, because the failure mode
        // of getting it wrong is a 413 with no field name to explain it. The
        // Express API caps the same bodies at 50 kB.
        services.Configure<KestrelServerOptions>(o => o.Limits.MaxRequestBodySize = MaxRequestBodyBytes);

        return services;
    }

    /// 1 MB. See the note in <see cref="AddApiInfrastructure"/>.
    internal const long MaxRequestBodyBytes = 1024 * 1024;

    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IDbConnectionFactory>(new SqlConnectionFactory(connectionString));

        // Better Auth owns sessions; this only reads them, which is why it is a
        // plain service rather than an authentication scheme.
        services.AddScoped<ISessionReader, DatabaseSessionReader>();

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

    /// The timed database ping, and the feature-flag infrastructure that gates it.
    ///
    /// The hosted service is registered unconditionally and asks the flag on each
    /// tick, rather than being registered only when the flag is on. Both are one
    /// line; only this one can be switched on without a redeploy, which is the
    /// point of shipping it dark.
    public static IServiceCollection AddDatabaseKeepAlive(
        this IServiceCollection services,
        IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Binds the "FeatureManagement" configuration section.
        services.AddFeatureManagement(config.GetSection("FeatureManagement"));

        services.AddHostedService<DatabaseKeepAliveService>();

        return services;
    }

    /// Statement PDFs live beside the database. The directory is derived from
    /// configuration rather than from the connection string, because a SQL Server
    /// connection string has no file path to derive it from — that inference only
    /// made sense when the database was a SQLite file.
    public static IServiceCollection AddStatementStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var directory = configuration["Statements:Directory"]
            ?? Path.Combine(environment.ContentRootPath, "statements");

        services.AddSingleton<IStatementStore>(new FileSystemStatementStore(directory));
        return services;
    }

    /// The two outbound HTTP dependencies. Typed clients rather than
    /// `IHttpClientFactory.CreateClient(name)`, so a slice cannot ask for the
    /// wrong one, and the base addresses live here rather than at every call site.
    public static IServiceCollection AddExternalClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var monzoOptions = configuration.GetSection(MonzoOptions.SectionName).Get<MonzoOptions>() ?? new MonzoOptions();
        services.AddSingleton(monzoOptions);

        services.AddHttpClient<IMonzoApiClient, MonzoApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.monzo.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient<IFxRateService, FrankfurterFxRateService>(client =>
        {
            client.BaseAddress = new Uri("https://api.frankfurter.dev/");
            // Short: an import converting hundreds of rows cannot wait 100 seconds
            // per lookup, and the fallback chain exists for exactly this.
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        return services;
    }

    /// One registration per slice. Explicit rather than assembly-scanned: a slice
    /// that forgets to appear here fails at startup instead of on first request.
    public static IServiceCollection AddFeatureSlices(this IServiceCollection services)
    {
        // Transactions
        services.AddScoped<GetTransactionsQuery>();
        services.AddScoped<UpdateTransactionCommand>();
        services.AddScoped<DeleteTransactionCommand>();

        // Categories
        services.AddScoped<GetCategoriesQuery>();
        services.AddScoped<CreateCategoryCommand>();
        services.AddScoped<UpdateCategoryCommand>();
        services.AddScoped<DeleteCategoryCommand>();
        services.AddScoped<MergeCategoriesCommand>();

        // Rules
        services.AddScoped<GetRulesQuery>();
        services.AddScoped<CreateRuleCommand>();
        services.AddScoped<UpdateRuleCommand>();
        services.AddScoped<ReorderRulesCommand>();
        services.AddScoped<DeleteRuleCommand>();
        services.AddScoped<PreviewRulesQuery>();
        services.AddScoped<ApplyRulesCommand>();

        // Recurring
        services.AddScoped<GetRecurringQuery>();
        services.AddScoped<SetRecurringVerdictCommand>();
        services.AddScoped<UpdateRecurringNoteCommand>();

        // Notes
        services.AddScoped<GetNotesQuery>();
        services.AddScoped<CreateNoteCommand>();
        services.AddScoped<UpdateNoteCommand>();
        services.AddScoped<DeleteNoteCommand>();

        // Tabs
        services.AddScoped<GetTabsQuery>();
        services.AddScoped<CreateTabCommand>();
        services.AddScoped<UpdateTabCommand>();
        services.AddScoped<DeleteTabCommand>();

        // Investments
        services.AddScoped<GetInvestmentsQuery>();
        services.AddScoped<CreateInvestmentAccountCommand>();
        services.AddScoped<UpdateInvestmentAccountCommand>();
        services.AddScoped<DeleteInvestmentAccountCommand>();
        services.AddScoped<UpsertInvestmentSnapshotCommand>();
        services.AddScoped<DeleteInvestmentSnapshotCommand>();
        services.AddScoped<DeleteInvestmentSnapshotsByDateCommand>();

        // Dashboard and utilities
        services.AddScoped<GetDashboardSummaryQuery>();
        services.AddScoped<GetDashboardAnalyticsQuery>();
        services.AddScoped<GetUtilitiesQuery>();

        // Users
        services.AddScoped<GetUsersQuery>();
        services.AddScoped<CreateUserCommand>();

        // Statements
        services.AddScoped<GetStatementsQuery>();
        services.AddScoped<GetStatementQuery>();
        services.AddScoped<DownloadStatementQuery>();
        services.AddScoped<DeleteStatementCommand>();

        // Import pipeline
        services.AddScoped<GetStagedCountsQuery>();
        services.AddScoped<GetLastStatementQuery>();
        services.AddScoped<ProcessStagedCommand>();
        services.AddScoped<BackfillUsdGbpCommand>();

        // Monzo
        services.AddScoped<MonzoConnectionResolver>();
        services.AddScoped<MonzoReconciler>();
        services.AddScoped<GetMonzoStatusQuery>();
        services.AddScoped<SyncMonzoCommand>();
        services.AddScoped<GetMonzoRecRunsQuery>();
        services.AddScoped<RunMonzoRecCommand>();

        // Dev
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
