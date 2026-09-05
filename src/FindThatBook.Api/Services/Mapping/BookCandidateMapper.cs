using FindThatBook.Api.Configuration;
using FindThatBook.Api.Models.DTO;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Services.Mapping
{
    /// <summary>
    /// Turns ranked works into the candidates the API returns.
    /// </summary>
    /// <remarks>
    /// Presentation only. The explanation arrives already written by whichever ranker ran, so
    /// that the text always reflects the evidence that actually decided the ordering rather
    /// than being reconstructed here from different reasoning.
    /// </remarks>
    public class BookCandidateMapper(IOptions<OpenLibraryOptions> openLibraryOptions) : IBookCandidateMapper
    {
        private readonly OpenLibraryOptions _options = openLibraryOptions.Value;

        public IReadOnlyList<BookCandidate> ToCandidates(BookRankingResult ranking) =>
            ranking.Ranked.Select(ToCandidate).ToArray();

        private BookCandidate ToCandidate(RankedWork ranked)
        {
            var work = ranked.Work;

            return new BookCandidate
            {
                Title = string.IsNullOrWhiteSpace(work.Subtitle)
                    ? work.Title
                    : $"{work.Title}: {work.Subtitle}",
                Authors = work.BestKnownAuthors,
                FirstPublishYear = work.FirstPublishYear,
                OpenLibraryKey = work.Key,
                OpenLibraryUrl = $"{_options.BaseUrl.TrimEnd('/')}{work.Key}",
                CoverImageUrl = work.CoverId is null
                    ? null
                    : $"{_options.CoverBaseUrl.TrimEnd('/')}/{work.CoverId}-M.jpg",
                Explanation = ranked.Explanation
            };
        }
    }
}
