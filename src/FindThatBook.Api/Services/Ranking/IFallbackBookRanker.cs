using FindThatBook.Api.Models.DTO;

namespace FindThatBook.Api.Services.Ranking
{
    public interface IFallbackBookRanker
    {
        /// <summary>
        /// Orders candidates by title and author evidence using fixed scoring rules, with no
        /// external calls. Used whenever the language model is unconfigured or unavailable.
        /// </summary>
        IReadOnlyList<RankedWork> Rank(ExtractedQuery query, IReadOnlyList<OpenLibraryWork> works);
    }
}
