using FindThatBook.Api.Models.DTO;

namespace FindThatBook.Api.Services.Retrieval
{
    public interface IBookRetrievalService
    {
        /// <summary>
        /// Finds candidate works for an interpreted query, resolving canonical primary
        /// authors where the budget allows.
        /// </summary>
        /// <param name="query">The structured reading of the user's query.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// Candidate works in Open Library relevance order, deduplicated by work key, plus
        /// the strategy that produced them.
        /// </returns>
        /// <exception cref="Exceptions.OpenLibraryException">Open Library failed or replied unusably.</exception>
        Task<BookRetrievalResult> RetrieveAsync(
            ExtractedQuery query,
            CancellationToken cancellationToken);
    }
}
