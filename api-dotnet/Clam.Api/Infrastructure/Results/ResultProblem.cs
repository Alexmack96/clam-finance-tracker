using Ardalis.Result;
using Microsoft.AspNetCore.Mvc;

namespace Clam.Api.Infrastructure.Results;

/// Turns a failed <see cref="Result{T}"/> into an RFC 7807 response.
///
/// Ardalis ships <c>ToMinimalApiResult()</c> for exactly this, and it is not used
/// here on purpose: it routes the *success* payload through
/// <c>Results.Ok(value)</c>, which serialises with ASP.NET's default JSON
/// options rather than the ones configured on FastEndpoints. Decimals would come
/// back as JSON numbers instead of the strings Prisma emits, and dates would lose
/// their trailing Z — the two things Infrastructure/Json exists to prevent, broken
/// silently and only on the endpoints that adopted the Result pattern.
///
/// So success stays on FastEndpoints' sender (see <see cref="ResultEndpoint{TRequest,TResponse}"/>)
/// and only the failure paths land here, where ProblemDetails has no domain
/// types in it and the default serialiser is the correct one.
internal static class ResultProblem
{
    /// Mirrors the status mapping in Ardalis.Result.AspNetCore so behaviour does
    /// not diverge from the documented table.
    internal static int StatusCodeFor(ResultStatus status) => status switch
    {
        ResultStatus.Invalid => StatusCodes.Status400BadRequest,
        ResultStatus.Unauthorized => StatusCodes.Status401Unauthorized,
        ResultStatus.Forbidden => StatusCodes.Status403Forbidden,
        ResultStatus.NotFound => StatusCodes.Status404NotFound,
        ResultStatus.Conflict => StatusCodes.Status409Conflict,
        ResultStatus.Error => StatusCodes.Status422UnprocessableEntity,
        ResultStatus.Unavailable => StatusCodes.Status503ServiceUnavailable,
        // CriticalError and anything added to the enum later.
        _ => StatusCodes.Status500InternalServerError,
    };

    internal static async Task WriteAsync(
        HttpContext context,
        ResultStatus status,
        IEnumerable<string> errors,
        IEnumerable<ValidationError> validationErrors,
        CancellationToken ct)
    {
        var statusCode = StatusCodeFor(status);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        // Invalid is the one status carrying per-field detail, so it gets the
        // richer shape. Everything else is a flat list of messages.
        if (status == ResultStatus.Invalid)
        {
            var grouped = validationErrors
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Identifier) ? "request" : e.Identifier)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray(), StringComparer.Ordinal);

            var validationProblem = new ValidationProblemDetails(grouped)
            {
                Status = statusCode,
                Title = "One or more validation errors occurred.",
                Instance = context.Request.Path,
            };
            validationProblem.Extensions["traceId"] = context.TraceIdentifier;

            await context.Response.WriteAsJsonAsync(
                validationProblem, options: null, contentType: "application/problem+json", ct);
            return;
        }

        var messages = errors.ToArray();
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = TitleFor(status),
            // Never surface raw error text on a 5xx — CriticalError messages are
            // written for the log, not for the caller.
            Detail = statusCode >= StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred. Please contact support."
                : messages.FirstOrDefault(),
            Instance = context.Request.Path,
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;

        if (messages.Length > 1 && statusCode < StatusCodes.Status500InternalServerError)
        {
            problem.Extensions["errors"] = messages;
        }

        await context.Response.WriteAsJsonAsync(
            problem, options: null, contentType: "application/problem+json", ct);
    }

    private static string TitleFor(ResultStatus status) => status switch
    {
        ResultStatus.Unauthorized => "Unauthorized",
        ResultStatus.Forbidden => "Forbidden",
        ResultStatus.NotFound => "Not Found",
        ResultStatus.Conflict => "Conflict",
        ResultStatus.Error => "Unprocessable Entity",
        ResultStatus.Unavailable => "Service Unavailable",
        _ => "Internal Server Error",
    };
}
