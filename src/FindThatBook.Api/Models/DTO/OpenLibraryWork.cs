namespace FindThatBook.Api.Models.DTO;

/// <summary>
/// A work as returned by Open Library's search endpoint, optionally enriched with canonical
/// author data from the work endpoint.
/// </summary>
/// <remarks>
/// Internal retrieval shape, not part of the API contract. The distinction between
/// <see cref="ContributorNames"/> and <see cref="PrimaryAuthorNames"/> is the whole point of
/// this type: search results list illustrators, editors, and adaptors alongside the actual
/// author, and treating those as equal evidence produces confidently wrong matches.
/// </remarks>
public sealed class OpenLibraryWork
{
    /// <summary>Work key, for example "/works/OL45804W".</summary>
    public required string Key { get; init; }

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    /// <summary>
    /// Every name Open Library lists against the work, in search-result order. Includes
    /// contributors, so it is weak evidence on its own.
    /// </summary>
    public IReadOnlyList<string> ContributorNames { get; init; } = [];

    /// <summary>Author keys, positionally aligned with <see cref="ContributorNames"/>.</summary>
    public IReadOnlyList<string> ContributorKeys { get; init; } = [];

    /// <summary>
    /// Names confirmed as primary authors by the canonical work record. Empty when the
    /// follow-up lookup was skipped or produced nothing.
    /// </summary>
    public IReadOnlyList<string> PrimaryAuthorNames { get; init; } = [];

    public int? FirstPublishYear { get; init; }

    /// <summary>Open Library cover id, used to build a cover image URL.</summary>
    public int? CoverId { get; init; }

    public int? EditionCount { get; init; }

    /// <summary>
    /// Best available authors: the canonical primary authors when we know them, otherwise
    /// the raw contributor list.
    /// </summary>
    public IReadOnlyList<string> BestKnownAuthors =>
        PrimaryAuthorNames.Count > 0 ? PrimaryAuthorNames : ContributorNames;

    /// <summary>True when primary authors were confirmed against the canonical work record.</summary>
    public bool HasConfirmedPrimaryAuthors => PrimaryAuthorNames.Count > 0;
}
