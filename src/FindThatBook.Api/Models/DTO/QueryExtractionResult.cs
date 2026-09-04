namespace FindThatBook.Api.Models.DTO;

/// <summary>
/// An <see cref="ExtractedQuery"/> together with the story of how it was produced.
/// </summary>
public sealed class QueryExtractionResult
{
    /// <summary>The structured reading of the query.</summary>
    public required ExtractedQuery Query { get; init; }

    /// <summary>Which path produced <see cref="Query"/>.</summary>
    public required QueryExtractionSource Source { get; init; }

    /// <summary>
    /// Why the deterministic parser was used, in words a client can display. Null whenever
    /// <see cref="Source"/> is <see cref="QueryExtractionSource.Llm"/>.
    /// </summary>
    public string? FallbackReason { get; init; }

    public static QueryExtractionResult FromLlm(ExtractedQuery query) =>
        new() { Query = query, Source = QueryExtractionSource.Llm };

    public static QueryExtractionResult FromFallback(ExtractedQuery query, string reason) =>
        new() { Query = query, Source = QueryExtractionSource.Fallback, FallbackReason = reason };
}
