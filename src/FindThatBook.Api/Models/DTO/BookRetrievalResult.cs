namespace FindThatBook.Api.Models.DTO;

/// <summary>How the candidates were found. Recorded so explanations can state the evidence
/// actually used rather than guessing at it.</summary>
public enum RetrievalStrategy
{
    /// <summary>Searched the title and author fields together.</summary>
    TitleAndAuthor,

    /// <summary>Searched the title field only.</summary>
    TitleOnly,

    /// <summary>Searched the author field only.</summary>
    AuthorOnly,

    /// <summary>No title or author was identified, so the terms were searched generally.</summary>
    Keywords,

    /// <summary>The field search found nothing, so every term was retried as one general query.</summary>
    WidenedAfterNoMatches
}

/// <summary>Candidate works plus the story of how they were found.</summary>
public sealed class BookRetrievalResult
{
    public required IReadOnlyList<OpenLibraryWork> Works { get; init; }

    public required RetrievalStrategy Strategy { get; init; }

    public static readonly BookRetrievalResult None =
        new() { Works = [], Strategy = RetrievalStrategy.Keywords };
}
