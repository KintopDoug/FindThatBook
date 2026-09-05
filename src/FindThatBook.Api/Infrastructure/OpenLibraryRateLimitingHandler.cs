using FindThatBook.Api.Exceptions;

namespace FindThatBook.Api.Infrastructure;

/// <summary>
/// Holds each outbound Open Library request until the shared rate limiter allows it.
/// </summary>
/// <remarks>
/// This lives in the HttpClient pipeline rather than in the client code so that it paces
/// every network attempt, retries included. The resilience handler is registered through
/// ConfigureHttpClientDefaults and therefore sits outside this one, so a retry re-enters here
/// and takes a fresh permit. Pacing only the first attempt would leave the retry storm after
/// a 429 completely unpaced, which is precisely when pacing matters most.
/// </remarks>
public sealed class OpenLibraryRateLimitingHandler(
    IOpenLibraryRateLimiter rateLimiter,
    ILogger<OpenLibraryRateLimitingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var waitedFrom = TimeProvider.System.GetTimestamp();

        using var lease = await rateLimiter.AcquireAsync(cancellationToken);

        if (!lease.IsAcquired)
        {
            // The queue is full. Waiting longer would only turn this into a timeout, and the
            // caller is better served by a prompt answer than a slow one.
            logger.LogWarning(
                "Rate limit queue is full; refusing {Method} {Uri}.",
                request.Method,
                request.RequestUri);

            throw new OpenLibraryException(
                "Too many book searches are in flight to reach Open Library right now.");
        }

        var waited = TimeProvider.System.GetElapsedTime(waitedFrom);

        if (waited > TimeSpan.FromMilliseconds(50))
        {
            logger.LogInformation(
                "Paced {Method} {Uri} by {WaitedMs}ms to stay within the Open Library rate limit.",
                request.Method,
                request.RequestUri,
                (int)waited.TotalMilliseconds);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
