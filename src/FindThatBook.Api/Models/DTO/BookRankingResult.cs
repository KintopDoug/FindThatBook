using System.Text.Json.Serialization;

namespace FindThatBook.Api.Models.DTO;

/// <summary>
/// Which path ordered the candidates and wrote their explanations.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RankingSource>))]
public enum RankingSource
{
    /// <summary>Ordered and explained by the language model.</summary>
    Llm,

    /// <summary>Ordered and explained by the built-in scoring rules.</summary>
    Fallback
}

/// <summary>A candidate work together with the reason it earned its position.</summary>
public sealed class RankedWork
{
    public required OpenLibraryWork Work { get; init; }

    /// <summary>
    /// Short, grounded sentence naming the evidence behind the match. Must describe evidence
    /// that actually exists in the query and the work data.
    /// </summary>
    public required string Explanation { get; init; }
}

/// <summary>Ranked candidates plus the story of how they were ordered.</summary>
public sealed class BookRankingResult
{
    public required IReadOnlyList<RankedWork> Ranked { get; init; }

    public required RankingSource Source { get; init; }

    /// <summary>
    /// Why the deterministic ranker was used, in words a client can display. Null whenever
    /// <see cref="Source"/> is <see cref="RankingSource.Llm"/>.
    /// </summary>
    public string? FallbackReason { get; init; }

    public static BookRankingResult FromLlm(IReadOnlyList<RankedWork> ranked) =>
        new() { Ranked = ranked, Source = RankingSource.Llm };

    public static BookRankingResult FromFallback(IReadOnlyList<RankedWork> ranked, string reason) =>
        new() { Ranked = ranked, Source = RankingSource.Fallback, FallbackReason = reason };
}
