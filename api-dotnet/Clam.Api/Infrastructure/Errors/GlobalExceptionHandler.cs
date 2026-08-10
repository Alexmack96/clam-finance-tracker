using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Clam.Api.Infrastructure.Errors;

/// Last line of defence. Anything a slice can *anticipate* should come back as a
/// failed <c>Result</c> instead — this exists for the genuinely unexpected, plus
/// the handful of SqlExceptions that are really domain answers wearing an
/// exception costume.
///
/// PremPoints detects a duplicate key by looking for "IX_" in the inner exception
/// message. That is a string match against text the driver is free to localise or
/// reword, and it fires on any index name that happens to appear in any message.
/// SqlException carries a stable numeric code, so this switches on that instead.
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    // https://learn.microsoft.com/sql/relational-databases/errors-events/database-engine-events-and-errors
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const int ConstraintViolation = 547;  // FK or CHECK
    private const int Deadlock = 1205;

    /// Not in Microsoft.AspNetCore.Http.StatusCodes — nginx's convention, and the
    /// usual choice for "the caller went away".
    private const int ClientClosedRequest = 499;

    // Azure SQL serverless auto-pauses. The first connection after a pause is
    // refused with one of these while the database resumes, which is a "try
    // again shortly", not a bug — see the note in Infrastructure/Data.
    private static readonly int[] TransientAzureCodes = [4060, 40197, 40501, 40613, 49918, 49919, 49920];

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var (statusCode, title, detail) = Classify(exception);

        // 5xx is a defect and gets a full stack trace; 4xx is the caller's
        // problem and would only be log noise at error level.
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception on {Method} {Path}. TraceId: {TraceId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                httpContext.TraceIdentifier);
        }
        else
        {
            logger.LogWarning(
                "Request failed with {StatusCode} on {Method} {Path}: {Title}. TraceId: {TraceId}",
                statusCode,
                httpContext.Request.Method,
                httpContext.Request.Path,
                title,
                httpContext.TraceIdentifier);
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path,
        };
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        // The generic 500 detail above is right for a deployment and useless for
        // debugging — a failing query returns "please contact support" and the
        // real message only reaches the log. In Development the exception rides
        // along, which is what makes an integration test failure diagnosable from
        // the assertion message alone.
        if (environment.IsDevelopment())
        {
            problemDetails.Extensions["exception"] = exception.ToString();
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(
            problemDetails, options: null, contentType: "application/problem+json", cancellationToken);

        return true;
    }

    private static (int StatusCode, string Title, string Detail) Classify(Exception exception) => exception switch
    {
        SqlException sql when sql.Number is UniqueIndexViolation or UniqueConstraintViolation
            => (StatusCodes.Status409Conflict, "Conflict",
                "A record with the same unique key already exists."),

        SqlException sql when sql.Number == ConstraintViolation
            => (StatusCodes.Status400BadRequest, "Constraint Violation",
                "The request references a row that does not exist, or a value the database rejects."),

        SqlException sql when sql.Number == Deadlock
            => (StatusCodes.Status409Conflict, "Deadlock",
                "The request was chosen as a deadlock victim. Retry it."),

        SqlException sql when TransientAzureCodes.Contains(sql.Number)
            => (StatusCodes.Status503ServiceUnavailable, "Database Unavailable",
                "The database is starting up or temporarily unavailable. Retry shortly."),

        // A client that hangs up mid-request is not a server error. Nothing is
        // written either way — the socket is gone — but this keeps it out of the
        // error logs and off the 5xx count.
        OperationCanceledException
            => (ClientClosedRequest, "Client Closed Request",
                "The request was cancelled by the caller."),

        _ => (StatusCodes.Status500InternalServerError, "Internal Server Error",
              "An unexpected error occurred. Please contact support."),
    };
}
