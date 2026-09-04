using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Models.DTO;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Services
{
    /// <summary>
    /// Turns an interpreted query into candidate works.
    /// </summary>
    /// <remarks>
    /// Two deliberate behaviours live here. First, a narrow search that finds nothing is
    /// retried more broadly: a wrong author guess should not turn into an empty page when a
    /// title search would have worked. Second, the top results are checked against the
    /// canonical work record so that illustrators and editors are not mistaken for authors.
    /// </remarks>
    public class BookRetrievalService(
        IOpenLibraryClient openLibraryClient,
        IOptions<OpenLibraryOptions> openLibraryOptions,
        ILogger<BookRetrievalService> logger) : IBookRetrievalService
    {
        private readonly OpenLibraryOptions _options = openLibraryOptions.Value;

        public async Task<BookRetrievalResult> RetrieveAsync(
            ExtractedQuery query,
            CancellationToken cancellationToken)
        {
            if (query.IsEmpty)
            {
                return BookRetrievalResult.None;
            }

            var (works, strategy) = await SearchAsync(query, cancellationToken);

            if (works.Count == 0)
            {
                return new BookRetrievalResult { Works = [], Strategy = strategy };
            }

            var deduplicated = Deduplicate(works);

            return new BookRetrievalResult
            {
                Works = await ResolvePrimaryAuthorsAsync(deduplicated, cancellationToken),
                Strategy = strategy
            };
        }

        /// <summary>
        /// Searches as specifically as the interpretation allows, then widens once if that
        /// found nothing.
        /// </summary>
        private async Task<(IReadOnlyList<OpenLibraryWork> Works, RetrievalStrategy Strategy)> SearchAsync(
            ExtractedQuery query,
            CancellationToken cancellationToken)
        {
            var keywords = query.Keywords.Count > 0 ? string.Join(' ', query.Keywords) : null;
            var hasTitle = !string.IsNullOrWhiteSpace(query.Title);
            var hasAuthor = !string.IsNullOrWhiteSpace(query.Author);
            var hasFieldTerms = hasTitle || hasAuthor;

            var strategy = (hasTitle, hasAuthor) switch
            {
                (true, true) => RetrievalStrategy.TitleAndAuthor,
                (true, false) => RetrievalStrategy.TitleOnly,
                (false, true) => RetrievalStrategy.AuthorOnly,
                _ => RetrievalStrategy.Keywords
            };

            // With a title or author, search those fields. With neither, the keywords are all
            // we have, so they become a general query.
            var works = hasFieldTerms
                ? await openLibraryClient.SearchWorksAsync(query.Title, query.Author, null, cancellationToken)
                : await openLibraryClient.SearchWorksAsync(null, null, keywords, cancellationToken);

            if (works.Count > 0 || !hasFieldTerms)
            {
                return (works, strategy);
            }

            // Nothing matched the specific fields. The interpretation may have mislabelled a
            // title as an author or vice versa, so try everything as one general query before
            // reporting no matches.
            var allTerms = string.Join(' ', new[] { query.Title, query.Author, keywords }
                .Where(term => !string.IsNullOrWhiteSpace(term)));

            logger.LogInformation(
                "Field search found nothing for title={Title} author={Author}; widening to {Terms}.",
                query.Title,
                query.Author,
                allTerms);

            var widened = await openLibraryClient.SearchWorksAsync(null, null, allTerms, cancellationToken);

            return (widened, RetrievalStrategy.WidenedAfterNoMatches);
        }

        private static IReadOnlyList<OpenLibraryWork> Deduplicate(IReadOnlyList<OpenLibraryWork> works) =>
            works
                .GroupBy(work => work.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

        /// <summary>
        /// Replaces the raw contributor list with confirmed primary authors for the leading
        /// candidates, within the configured lookup budget.
        /// </summary>
        private async Task<IReadOnlyList<OpenLibraryWork>> ResolvePrimaryAuthorsAsync(
            IReadOnlyList<OpenLibraryWork> works,
            CancellationToken cancellationToken)
        {
            var budget = Math.Min(_options.PrimaryAuthorLookups, works.Count);

            if (budget == 0)
            {
                return works;
            }

            var resolved = works.ToArray();

            for (var index = 0; index < budget; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var work = resolved[index];

                // Without keys we cannot line author ids up with names, so there is nothing
                // to confirm against.
                if (work.ContributorKeys.Count == 0)
                {
                    continue;
                }

                IReadOnlyList<string> primaryKeys;
                try
                {
                    primaryKeys = await openLibraryClient.GetPrimaryAuthorKeysAsync(work.Key, cancellationToken);
                }
                catch (OpenLibraryException exception)
                {
                    // Enrichment is an improvement, not a requirement. A failed lookup costs
                    // us author precision for one candidate, not the whole search.
                    logger.LogWarning(
                        exception,
                        "Could not confirm primary authors for {WorkKey}; using the listed names.",
                        work.Key);

                    continue;
                }

                var primaryNames = MatchNames(work, primaryKeys);

                if (primaryNames.Count > 0)
                {
                    resolved[index] = With(work, primaryNames);
                }
            }

            return resolved;
        }

        /// <summary>
        /// Maps canonical author keys onto names using the positionally aligned key and name
        /// arrays from the search result, which avoids a request per author.
        /// </summary>
        private static IReadOnlyList<string> MatchNames(
            OpenLibraryWork work,
            IReadOnlyList<string> primaryKeys)
        {
            if (primaryKeys.Count == 0)
            {
                return [];
            }

            var wanted = new HashSet<string>(primaryKeys, StringComparer.OrdinalIgnoreCase);
            var names = new List<string>();

            for (var index = 0; index < work.ContributorKeys.Count; index++)
            {
                // Open Library does not guarantee the two arrays are the same length.
                if (index >= work.ContributorNames.Count)
                {
                    break;
                }

                if (wanted.Contains(work.ContributorKeys[index]))
                {
                    names.Add(work.ContributorNames[index]);
                }
            }

            return names;
        }

        private static OpenLibraryWork With(OpenLibraryWork work, IReadOnlyList<string> primaryAuthorNames) =>
            new()
            {
                Key = work.Key,
                Title = work.Title,
                Subtitle = work.Subtitle,
                ContributorNames = work.ContributorNames,
                ContributorKeys = work.ContributorKeys,
                PrimaryAuthorNames = primaryAuthorNames,
                FirstPublishYear = work.FirstPublishYear,
                CoverId = work.CoverId,
                EditionCount = work.EditionCount
            };
    }
}
