using FindThatBook.Api.Models.DTO;

namespace FindThatBook.Api.Services
{
    public interface ILlmBookRanker
    {
        /// <summary>
        /// Asks the language model to order candidates by how well they answer the query and
        /// to explain each one.
        /// </summary>
        /// <param name="rawQuery">The normalized query as the user typed it.</param>
        /// <param name="query">The structured reading of that query.</param>
        /// <param name="works">Candidates retrieved from Open Library.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="Exceptions.LlmRankingException">
        /// The model could not be reached, or returned something unusable.
        /// </exception>
        Task<IReadOnlyList<RankedWork>> RankAsync(
            string rawQuery,
            ExtractedQuery query,
            IReadOnlyList<OpenLibraryWork> works,
            CancellationToken cancellationToken);
    }
}
