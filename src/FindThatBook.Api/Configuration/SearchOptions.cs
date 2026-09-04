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
}
