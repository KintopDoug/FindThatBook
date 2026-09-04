using FindThatBook.Api.Configuration;
using FindThatBook.Api.Models.DTO;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Services
{
    /// <summary>
    /// Turns retrieved works into the candidates the API returns.
    /// </summary>
    /// <remarks>
    /// The explanations here describe retrieval evidence only: which fields were searched,
    /// and whether the authors were confirmed against the canonical work record. They
    /// deliberately do not claim match strength, because nothing has scored these results
    /// yet -- the order is still Open Library's own relevance. The ranking stage will replace
    /// these with explanations that reflect scoring.
    /// </remarks>
    public class BookCandidateMapper(IOptions<OpenLibraryOptions> openLibraryOptions)
    {
        private readonly OpenLibraryOptions _options = openLibraryOptions.Value;

        public IReadOnlyList<BookCandidate> ToCandidates(BookRetrievalResult retrieval) =>
            retrieval.Works
                .Select(work => ToCandidate(work, retrieval.Strategy))
                .ToArray();

        private BookCandidate ToCandidate(OpenLibraryWork work, RetrievalStrategy strategy) =>
            new()
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
                Explanation = Explain(work, strategy)
            };

        private static string Explain(OpenLibraryWork work, RetrievalStrategy strategy)
        {
            var found = strategy switch
            {
                RetrievalStrategy.TitleAndAuthor => "Found by searching title and author.",
                RetrievalStrategy.TitleOnly => "Found by searching title.",
                RetrievalStrategy.AuthorOnly => "Found by searching author.",
                RetrievalStrategy.Keywords => "Found by keyword search; no title or author was identified.",
                _ => "Found by a broader keyword search after the title and author search returned nothing."
            };

            if (work.HasConfirmedPrimaryAuthors)
            {
                return $"{found} Primary author confirmed from the Open Library work record.";
            }

            return work.ContributorNames.Count > 0
                ? $"{found} Listed names are unconfirmed and may include contributors."
                : found;
        }
    }
}
