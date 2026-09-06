using System.ComponentModel.DataAnnotations;

namespace FindThatBook.Api.Configuration;

/// <summary>
/// Settings for the Open Library calls that find candidate books, bound from the
/// "OpenLibrary" configuration section.
/// </summary>
public sealed class OpenLibraryOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "OpenLibrary";

    /// <summary>Base address of the Open Library API.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "OpenLibrary:BaseUrl must be configured.")]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Base address for cover images, which are served from a separate host.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "OpenLibrary:CoverBaseUrl must be configured.")]
    public string CoverBaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Sent on every request. Open Library asks anonymous clients to identify themselves and
    /// throttles traffic that does not, so this is not decoration.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "OpenLibrary:UserAgent must be configured.")]
    public string UserAgent { get; init; } = string.Empty;

    /// <summary>
    /// Budget for one logical call, covering every retry attempt rather than each one.
    /// </summary>
    /// <remarks>
    /// The resilience handler retries inside a single HttpClient call, so this timeout wraps
    /// the whole sequence. Backoff alone costs about ten seconds, and honouring a Retry-After
    /// costs whatever Open Library asks for, so a value that looks generous for one request
    /// can silently cancel a retry that was about to succeed.
    /// <para>
    /// Two bounds matter. It must exceed the handler's 20 second attempt timeout, or our own
    /// cancellation kills a slow call that was about to return. And it must stay under the
    /// handler's 60 second total, so a timeout surfaces through our own cancellation path
    /// rather than as a Polly rejection. Both are set in ServiceDefaults.
    /// </para>
    /// </remarks>
    [Range(21, 59, ErrorMessage = "OpenLibrary:TotalTimeoutSeconds must be configured between 21 and 59.")]
    public int TotalTimeoutSeconds { get; init; }

    /// <summary>How many works to ask the search endpoint for.</summary>
    [Range(1, 100, ErrorMessage = "OpenLibrary:MaxResults must be configured between 1 and 100.")]
    public int MaxResults { get; init; }

    /// <summary>
    /// How many of the returned works to follow up with a work-detail call in order to
    /// identify canonical primary authors. Each one is an extra request, so this trades
    /// latency for author accuracy. Zero disables the follow-up entirely.
    /// </summary>
    [Range(0, 25, ErrorMessage = "OpenLibrary:PrimaryAuthorLookups must be configured between 0 and 25.")]
    public int PrimaryAuthorLookups { get; init; }

    /// <summary>
    /// Sustained outbound request rate. Open Library allows one request per second for
    /// unidentified clients and three for clients that identify themselves in the User-Agent,
    /// which <see cref="UserAgent"/> does.
    /// </summary>
    [Range(1, 10, ErrorMessage = "OpenLibrary:RequestsPerSecond must be configured between 1 and 10.")]
    public int RequestsPerSecond { get; init; }

    /// <summary>
    /// How many requests may wait for a slot before we start rejecting.
    /// </summary>
    /// <remarks>
    /// This is a latency budget, not a capacity dial. At the configured rate the last request
    /// in a full queue waits roughly MaxQueuedRequests / RequestsPerSecond seconds, so a large
    /// value simply converts a fast rejection into a slow timeout.
    /// </remarks>
    [Range(0, 500, ErrorMessage = "OpenLibrary:MaxQueuedRequests must be configured between 0 and 500.")]
    public int MaxQueuedRequests { get; init; }

    /// <summary>
    /// How long a retrieval stays cached. Long enough to absorb repeated and concurrent
    /// searches for the same book, short enough that catalogue edits show up the same day.
    /// </summary>
    [Range(1, 1440, ErrorMessage = "OpenLibrary:CacheLifetimeMinutes must be configured between 1 and 1440.")]
    public int CacheLifetimeMinutes { get; init; }

    /// <summary>
    /// Ceiling on cached retrievals. Without one the cache grows with the number of distinct
    /// queries, which is unbounded when the queries come from users.
    /// </summary>
    [Range(1, 100_000, ErrorMessage = "OpenLibrary:CacheMaxEntries must be configured between 1 and 100000.")]
    public int CacheMaxEntries { get; init; }
}
