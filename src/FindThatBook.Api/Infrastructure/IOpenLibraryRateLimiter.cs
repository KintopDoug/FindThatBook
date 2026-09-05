using System.Threading.RateLimiting;

namespace FindThatBook.Api.Infrastructure;

public interface IOpenLibraryRateLimiter
{
    /// <summary>
    /// Total permits handed out, meaning the number of requests actually released to Open
    /// Library. Because this counts every attempt rather than every logical call, it also
    /// reveals whether retries are being paced.
    /// </summary>
    long PermitsGranted { get; }

    /// <summary>
    /// Waits for permission to make one request.
    /// </summary>
    /// <returns>
    /// A lease that must be disposed. <see cref="RateLimitLease.IsAcquired"/> is false when
    /// the queue is full, which is a signal to give up rather than to wait.
    /// </returns>
    ValueTask<RateLimitLease> AcquireAsync(CancellationToken cancellationToken);
}
