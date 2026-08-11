using Ardalis.Result;
using Clam.Api.Infrastructure.Results;

namespace Clam.Api.Features.Monzo.RunMonzoRec;

/// Runs a reconciliation on demand rather than as a side effect of a sync.
/// Deliberately the same pass — a "manual" trigger changes only what the run
/// records about why it happened.
public sealed class RunMonzoRecCommand(MonzoConnectionResolver resolver, MonzoReconciler reconciler)
{
    public async Task<Result<MonzoRecRun>> ExecuteAsync(CancellationToken ct)
    {
        var resolved = await resolver.ResolveAsync(ct);
        if (!resolved.IsSuccess) return resolved.PropagateFailure<MonzoConnectionResolver.Connection, MonzoRecRun>();

        var run = await reconciler.ReconcileAsync(
            resolved.Value.Accounts, resolved.Value.Credential.AccessToken, "manual", ct);

        return Result<MonzoRecRun>.Success(run);
    }
}
