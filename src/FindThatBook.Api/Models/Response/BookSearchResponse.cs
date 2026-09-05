using FindThatBook.Api.Models.DTO;

namespace FindThatBook.Api.Models.Response;

/// <summary>
/// Response body for the book search endpoint.
/// </summary>
public sealed class BookSearchResponse
{
    /// <summary>The normalized query that was actually searched, echoed back for display.</summary>
    public required string Query { get; init; }

    /// <summary>How the query was read: the title, author, and keywords searched on.</summary>
    public required ExtractedQuery Interpretation { get; init; }

    /// <summary>
    /// Whether the interpretation came from the language model or the deterministic parser.
    /// </summary>
    public required QueryExtractionSource ExtractionSource { get; init; }

    /// <summary>
    /// Plain-language reason the deterministic parser was used, suitable for display.
    /// Null when the language model produced the interpretation.
    /// </summary>
    public string? ExtractionFallbackReason { get; init; }

    /// <summary>
    /// Whether the ordering and explanations came from the language model or the built-in
    /// rules. Null when there were no results to order.
    /// </summary>
    public RankingSource? RankingSource { get; init; }

    /// <summary>
    /// Plain-language reason the deterministic ranker was used, suitable for display.
    /// Null when the language model produced the ranking, or when nothing was ranked.
    /// </summary>
    public string? RankingFallbackReason { get; init; }

    /// <summary>
    /// Candidates in rank order, best first. Empty when the query was understood but
    /// nothing plausible was found, which is a 200 rather than an error.
    /// </summary>
    public IReadOnlyList<BookCandidate> Results { get; init; } = [];
}
