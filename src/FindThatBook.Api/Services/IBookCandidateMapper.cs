using FindThatBook.Api.Models.DTO;

namespace FindThatBook.Api.Services
{
    public interface IBookCandidateMapper
    {
        /// <summary>
        /// Turns ranked works into the candidates the API returns, preserving rank order and
        /// the explanation each ranker produced.
        /// </summary>
        IReadOnlyList<BookCandidate> ToCandidates(BookRankingResult ranking);
    }
}
