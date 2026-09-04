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
    /// can silently cancel a retry that was about to succeed. It must also stay below the
    /// handler's own 30 second total, so that a timeout surfaces through our own cancellation
    /// path rather than as a Polly rejection.
    /// </remarks>
    [Range(1, 29, ErrorMessage = "OpenLibrary:TotalTimeoutSeconds must be configured between 1 and 29.")]
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
}
