using FindThatBook.Api.Models.DTO;

namespace FindThatBook.Api.Services
{
    public interface IQueryExtractionService
    {
        /// <summary>
        /// Turns a normalized query into structured search fields, using the language model
        /// when it is available and the deterministic parser when it is not.
        /// </summary>
        /// <param name="query">A normalized, non-empty query.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The interpretation, and which path produced it.</returns>
        Task<QueryExtractionResult> ExtractAsync(string query, CancellationToken cancellationToken);
    }
}
