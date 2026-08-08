using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Dev.SeedData;

/// Registered only when Seed:Enabled is true — see Program.cs. Reseeding wipes
/// both tables, so it is opt-in rather than merely non-production: pointing this
/// at a database that matters should take a deliberate config change.
///
/// The only endpoint on a ResultEndpoint base so far, because it is the only one
/// with a failure the caller can do something about. The read slices stay on
/// plain Endpoint&lt;,&gt; — a Result around a query that cannot fail is a branch
/// that never runs.
public sealed class SeedDataEndpoint(SeedDataCommand command)
    : ResultEndpoint<SeedDataRequest, SeedDataResponse>
{
    public override void Configure()
    {
        Post("dev/seed");
        AllowAnonymous();
        Description(b => b
            .WithName("SeedDevData")
            .Produces<SeedDataResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict));
    }

    public override async Task HandleAsync(SeedDataRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req.Count, ct), ct);
}
