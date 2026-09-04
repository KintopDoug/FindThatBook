namespace FindThatBook.Api.Models.DTO;

/// <summary>
/// A single candidate book returned for a query, carrying the fields a user needs to
/// recognise the result and judge why it was matched.
/// </summary>
public sealed class BookCandidate
{
    /// <summary>Work title as reported by Open Library.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// Primary author(s). Contributors such as illustrators or editors are excluded here,
    /// so this may be empty when Open Library exposes no primary author.
    /// </summary>
    public IReadOnlyList<string> Authors { get; init; } = [];

    /// <summary>Earliest known publication year, when Open Library reports one.</summary>
    public int? FirstPublishYear { get; init; }

    /// <summary>Open Library work key, for example "/works/OL45804W".</summary>
    public string? OpenLibraryKey { get; init; }

    /// <summary>Absolute link to the work on openlibrary.org.</summary>
    public string? OpenLibraryUrl { get; init; }

    /// <summary>Cover image URL, when a cover is available.</summary>
    public string? CoverImageUrl { get; init; }

    /// <summary>
    /// Short, grounded sentence describing the evidence behind this match, for example
    /// "Strong title and primary-author match."
    /// </summary>
    public required string Explanation { get; init; }
}
