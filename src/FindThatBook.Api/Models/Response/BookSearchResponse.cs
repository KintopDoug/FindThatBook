using FindThatBook.Api.Models.DTO;

namespace FindThatBook.Api.Models.Response;

/// <summary>
/// Response body for the book search endpoint.
/// </summary>
public sealed class BookSearchResponse
{
    /// <summary>The normalized query that was actually searched, echoed back for display.</summary>
    public required string Query { get; init; }

    /// <summary>
    /// Candidates in rank order, best first. Empty when the query was understood but
    /// nothing plausible was found, which is a 200 rather than an error.
    /// </summary>
    public IReadOnlyList<BookCandidate> Results { get; init; } = [];
}
