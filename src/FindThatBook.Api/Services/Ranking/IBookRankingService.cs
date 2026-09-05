using FindThatBook.Api.Models.DTO;

namespace FindThatBook.Api.Services.Ranking
{
    public interface IBookRankingService
    {
        /// <summary>
        /// Orders candidates and explains each one, using the language model when it is
        /// available and the deterministic ranker when it is not.
        /// </summary>
        /// <param name="rawQuery">The normalized query as the user typed it.</param>
        /// <param name="query">The structured reading of that query.</param>
        /// <param name="works">
        /// Candidates retrieved from Open Library. Must not be empty: with nothing to order
        /// no ranking happens at all, and the caller reports that rather than inventing a
        /// source for it.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The ranking, and which path produced it.</returns>
        Task<BookRankingResult> RankAsync(
            string rawQuery,
            ExtractedQuery query,
            IReadOnlyList<OpenLibraryWork> works,
            CancellationToken cancellationToken);
    }
}
