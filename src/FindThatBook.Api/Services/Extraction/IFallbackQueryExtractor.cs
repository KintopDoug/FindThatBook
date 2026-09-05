using FindThatBook.Api.Models.DTO;

namespace FindThatBook.Api.Services.Extraction
{
    public interface IFallbackQueryExtractor
    {
        /// <summary>
        /// Interprets a normalized query using fixed rules, with no external calls. Used
        /// whenever the language model is unconfigured or unavailable.
        /// </summary>
        /// <param name="query">A normalized, non-empty query.</param>
        ExtractedQuery Extract(string query);
    }
}
