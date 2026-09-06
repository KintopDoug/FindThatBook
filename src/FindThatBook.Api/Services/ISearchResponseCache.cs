using FindThatBook.Api.Models.Response;

namespace FindThatBook.Api.Services
{
    public interface ISearchResponseCache
    {
        /// <summary>
        /// Looks for a finished response to an equivalent query.
        /// </summary>
        /// <param name="normalizedQuery">The validated, normalized query.</param>
        /// <param name="response">The cached response, or null on a miss.</param>
        bool TryGet(string normalizedQuery, out BookSearchResponse? response);

        /// <summary>Remembers a finished response for the configured lifetime.</summary>
        void Set(string normalizedQuery, BookSearchResponse response);
    }
}
