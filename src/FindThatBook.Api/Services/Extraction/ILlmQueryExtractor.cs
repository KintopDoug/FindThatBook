using FindThatBook.Api.Models.DTO;

namespace FindThatBook.Api.Services.Extraction
{
    public interface ILlmQueryExtractor
    {
        /// <summary>
        /// Asks the language model to split a normalized query into title, author, and
        /// keywords.
        /// </summary>
        /// <param name="query">A normalized, non-empty query.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The model's reading of the query.</returns>
        /// <exception cref="Exceptions.LlmExtractionException">
        /// The model could not be reached, or returned something unusable.
        /// </exception>
        Task<ExtractedQuery> ExtractAsync(string query, CancellationToken cancellationToken);
    }
}
