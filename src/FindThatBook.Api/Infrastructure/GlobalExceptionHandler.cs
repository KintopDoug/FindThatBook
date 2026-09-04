using System.Diagnostics;
using FindThatBook.Api.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FindThatBook.Api.Infrastructure;

/// <summary>
/// Converts any unhandled exception into an RFC 9457 ProblemDetails response, so clients
/// always get a readable JSON body instead of an empty 500 or a raw HTML error page.
/// </summary>
/// <remarks>
/// Exception text is only echoed to the client outside of production. Messages routinely
/// contain connection strings, file paths, and provider keys, so production callers get a
/// generic description plus the traceId needed to find the real error in the logs.
/// </remarks>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // The caller hung up. There is no one left to read a response body, and this is
        // not a fault worth reporting as an error.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation(
                "Request {Method} {Path} was cancelled by the client.",
                httpContext.Request.Method,
                httpContext.Request.Path);

            return true;
        }

        var (statusCode, title, publicDetail) = Describe(exception);

        // 5xx means we are at fault, 4xx generally means the caller is; log accordingly so
        // the dashboard's error filter stays meaningful.
        logger.Log(
            statusCode >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Warning,
            exception,
            "Unhandled {ExceptionType} on {Method} {Path}.",
            exception.GetType().Name,
            httpContext.Request.Method,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = environment.IsProduction() ? publicDetail : exception.Message,
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
        };

        // Correlates the response with the Aspire dashboard's traces and structured logs.
        problemDetails.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (!environment.IsProduction())
        {
            problemDetails.Extensions["exceptionType"] = exception.GetType().FullName;

            // ToString() rather than StackTrace, so inner exceptions come along too.
            problemDetails.Extensions["stackTrace"] = exception.ToString();
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    /// <summary>
    /// Maps an exception to a status code, a title, and a detail message that is safe to
    /// return in production. Deliberately narrow: only failures we can genuinely attribute
    /// are mapped, and anything else stays a 500 rather than masking a bug as a 4xx.
    /// </summary>
    private static (int StatusCode, string Title, string PublicDetail) Describe(Exception exception) =>
        exception switch
        {
            // Caller error. The message is about their input and leaks nothing internal, so
            // it is returned verbatim in production rather than replaced with a generic one.
            InvalidQueryException => (
                StatusCodes.Status400BadRequest,
                "Invalid query",
                exception.Message),

            // Open Library failed. Unlike LLM extraction there is no fallback for this: with
            // no catalogue there are no candidates to return.
            OpenLibraryException => (
                StatusCodes.Status502BadGateway,
                "Book search is unavailable",
                "The book catalogue could not be reached. Please try again shortly."),

            // Open Library (or another downstream call) was reachable but failed.
            HttpRequestException => (
                StatusCodes.Status502BadGateway,
                "Upstream service error",
                "An external service required to answer this request failed."),

            // Downstream call exceeded its budget, including resilience-handler timeouts.
            TimeoutException or TaskCanceledException => (
                StatusCodes.Status504GatewayTimeout,
                "Upstream service timeout",
                "An external service required to answer this request did not respond in time."),

            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred",
                "An unexpected error occurred while processing the request.")
        };
}
