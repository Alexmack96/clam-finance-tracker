using Ardalis.Result;
using FastEndpoints;

namespace Clam.Api.Infrastructure.Results;

/// Base classes for endpoints whose slice returns a <see cref="Result{T}"/>.
///
/// Why a base class rather than an extension method: FastEndpoints exposes its
/// senders through the protected `Send` member on the endpoint, so only a
/// derived type can reach it, and keeping success on that sender is the whole
/// point (see <see cref="ResultProblem"/>).
///
/// Endpoints that cannot fail — the current read slices — should keep inheriting
/// plain <c>Endpoint&lt;,&gt;</c>. Wrapping a query that only ever succeeds in a
/// Result adds a branch that can never be taken.
public abstract class ResultEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected Task SendResultAsync(Result<TResponse> result, CancellationToken ct)
        => this.SendAsResultAsync(result, ct);
}

/// The GET-shaped counterpart, for slices with no request DTO.
public abstract class ResultEndpointWithoutRequest<TResponse> : EndpointWithoutRequest<TResponse>
{
    protected Task SendResultAsync(Result<TResponse> result, CancellationToken ct)
        => this.SendAsResultAsync(result, ct);
}

internal static class ResultEndpointExtensions
{
    /// Shared by both base classes above. `Send` is not reachable from here, so
    /// the caller passes itself in and this uses the response-sending extensions
    /// on HttpContext.Response, which are the same methods `Send.*Async`
    /// delegates to and therefore use the configured FastEndpoints serialiser.
    internal static async Task SendAsResultAsync<TResponse>(
        this IEndpoint endpoint,
        Result<TResponse> result,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(result);

        var httpContext = endpoint.HttpContext;

        if (!result.IsSuccess)
        {
            await ResultProblem.WriteAsync(
                httpContext, result.Status, result.Errors, result.ValidationErrors, ct);
            return;
        }

        if (result.Status == ResultStatus.NoContent)
        {
            await httpContext.Response.SendNoContentAsync(ct);
            return;
        }

        var statusCode = result.Status == ResultStatus.Created
            ? StatusCodes.Status201Created
            : StatusCodes.Status200OK;

        await httpContext.Response.SendAsync(result.Value, statusCode, cancellation: ct);
    }
}
