using System.Threading.RateLimiting;
using FindThatBook.Api.Configuration;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Infrastructure;

/// <summary>
/// Process-wide pacing for outbound Open Library requests.
/// </summary>
/// <remarks>
/// Registered as a singleton, which is the whole point: the rate limit belongs to Open
/// Library, not to one inbound request. Ten users searching at once share this one budget,
/// so the limiter has to sit outside request scope to see them all.
/// <para>
/// Requests are spaced evenly rather than allowed to burst.
/// </para>
/// <para>
/// The interval carries deliberate headroom because we control when a request is sent while
/// Open Library counts when it arrives. Network jitter can bunch evenly-spaced sends together
/// at the far end, so aiming exactly at the limit would sometimes exceed it.
/// </para>
/// </remarks>
public sealed class OpenLibraryRateLimiter : IDisposable
{
    /// <summary>
    /// Fraction of extra spacing beyond the nominal rate, absorbing jitter between send time
    /// and arrival time.
    /// </summary>
    private const double SpacingHeadroom = 1.1;

    private readonly TokenBucketRateLimiter _limiter;
    private long _permitsGranted;

    /// <summary>
    /// Total permits handed out, meaning the number of requests actually released to Open
    /// Library. Because this counts every attempt rather than every logical call, it also
    /// reveals whether retries are being paced.
    /// </summary>
    public long PermitsGranted => Interlocked.Read(ref _permitsGranted);

    public OpenLibraryRateLimiter(IOptions<OpenLibraryOptions> openLibraryOptions)
    {
        var options = openLibraryOptions.Value;

        var spacing = TimeSpan.FromMilliseconds(
            1000.0 / options.RequestsPerSecond * SpacingHeadroom);

        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            // One token, never more: nothing accumulates while idle, so there is no burst to
            // spend later. This is what bounds any one-second span to the allowance.
            TokenLimit = 1,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = spacing,
            QueueLimit = options.MaxQueuedRequests,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    }

    /// <summary>
    /// Waits for permission to make one request.
    /// </summary>
    /// <returns>
    /// A lease that must be disposed. <see cref="RateLimitLease.IsAcquired"/> is false when
    /// the queue is full, which is a signal to give up rather than to wait.
    /// </returns>
    public async ValueTask<RateLimitLease> AcquireAsync(CancellationToken cancellationToken)
    {
        var lease = await _limiter.AcquireAsync(permitCount: 1, cancellationToken);

        if (lease.IsAcquired)
        {
            Interlocked.Increment(ref _permitsGranted);
        }

        return lease;
    }

    public void Dispose() => _limiter.Dispose();
}
