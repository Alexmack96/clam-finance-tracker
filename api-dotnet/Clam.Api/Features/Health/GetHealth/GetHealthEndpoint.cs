using FastEndpoints;

namespace Clam.Api.Features.Health.GetHealth;

/// Deliberately does not touch the database. Railway restarts a container whose
/// healthcheck fails, so a probe that depends on an auto-pausing Azure SQL
/// serverless database would turn a cold start into a restart loop.
public sealed class GetHealthEndpoint : EndpointWithoutRequest<GetHealthResponse>
{
    public override void Configure()
    {
        Get("health");

        // A probe has no credentials and Railway restarts a container whose
        // probe 401s.
        AllowAnonymous();
        Description(b => b.WithName("GetHealth"));
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await Send.OkAsync(new GetHealthResponse(), ct);
}
