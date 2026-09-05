using FindThatBook.Api.Models.DTO;

namespace FindThatBook.Api.Services.Retrieval
{
    public interface IBookRetrievalCache
    {
        /// <summary>
        /// Looks for a previous retrieval of an equivalent interpreted query.
        /// </summary>
        /// <param name="query">The interpreted query to look up.</param>
        /// <param name="result">The cached retrieval, or null on a miss.</param>
        /// <returns>True when a usable entry was found.</returns>
        bool TryGet(ExtractedQuery query, out BookRetrievalResult? result);

        /// <summary>Remembers a completed retrieval for the configured lifetime.</summary>
        void Set(ExtractedQuery query, BookRetrievalResult result);
    }
}
