using System.Text.Json.Serialization;

namespace FindThatBook.Api.Models.DTO;

/// <summary>
/// The structured reading of a raw user query: the parts we can turn into an Open Library
/// request. Every field is optional because a sparse query like "dickens" legitimately
/// yields an author and nothing else.
/// </summary>
public sealed class ExtractedQuery
{
    /// <summary>Book title, if the query appears to name one.</summary>
    public string? Title { get; init; }

    /// <summary>Author name, if the query appears to name one.</summary>
    public string? Author { get; init; }

    /// <summary>
    /// Remaining meaningful terms: subjects, genres, or descriptive words that are neither
    /// title nor author, used to broaden the search when title and author are weak.
    /// </summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>
    /// True when nothing usable could be pulled out of the query. Internal decision helper,
    /// not part of the wire contract.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Title)
        && string.IsNullOrWhiteSpace(Author)
        && Keywords.Count == 0;
}
