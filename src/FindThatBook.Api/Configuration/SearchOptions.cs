using System.ComponentModel.DataAnnotations;

namespace FindThatBook.Api.Configuration;

/// <summary>
/// Tuning knobs for the book search endpoint, bound from the "Search" configuration section.
/// </summary>
public sealed class SearchOptions
{
    /// <summary>Configuration section these options are bound from.</summary>
    public const string SectionName = "Search";

    /// <summary>
    /// Upper bound on accepted query length, to keep obviously junk input out of the pipeline.
    /// Required: the value lives in appsettings.json, and startup fails if it is missing.
    /// </summary>
    [Range(1, 4000, ErrorMessage =
        "Search:MaxQueryLength must be configured with a value between 1 and 4000.")]
    public int MaxQueryLength { get; init; }

    /// <summary>
    /// How long a finished search response stays cached.
    /// </summary>
    /// <remarks>
    /// Governs the cache that fronts the whole pipeline, so a repeat query costs no model
    /// call and no catalogue request. Shorter than the retrieval cache's lifetime because
    /// this one pins the AI's interpretation and wording, not just catalogue data.
    /// </remarks>
    [Range(1, 1440, ErrorMessage =
        "Search:ResponseCacheLifetimeMinutes must be configured between 1 and 1440.")]
    public int ResponseCacheLifetimeMinutes { get; init; }

    /// <summary>
    /// Ceiling on cached responses. Without one the cache grows with the number of distinct
    /// queries, which is unbounded when the queries come from users.
    /// </summary>
    [Range(1, 100_000, ErrorMessage =
        "Search:ResponseCacheMaxEntries must be configured between 1 and 100000.")]
    public int ResponseCacheMaxEntries { get; init; }
}
