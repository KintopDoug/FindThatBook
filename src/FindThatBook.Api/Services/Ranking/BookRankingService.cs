using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Models.DTO;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Services.Ranking
{
    /// <summary>
    /// Chooses how candidates get ordered and explained: the language model when it is
    /// configured and healthy, the deterministic ranker otherwise.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="FindThatBook.Api.Services.Extraction.QueryExtractionService"/> deliberately. Degrading is never silent:
    /// the chosen path and the reason travel back on the result, because a fallback ranking is
    /// a weaker answer that would otherwise look identical to a good one.
    /// </remarks>
    public class BookRankingService(
        ILlmBookRanker llmBookRanker,
        IFallbackBookRanker fallbackBookRanker,
        IOptions<GeminiOptions> geminiOptions,
        ILogger<BookRankingService> logger) : IBookRankingService
    {
        private const string NotConfiguredReason =
            "AI ranking is not configured, so results were ordered by built-in rules.";

        private const string UnavailableReason =
            "AI ranking was unavailable, so results were ordered by built-in rules.";

        private readonly GeminiOptions _geminiOptions = geminiOptions.Value;

        public async Task<BookRankingResult> RankAsync(
            string rawQuery,
            ExtractedQuery query,
            IReadOnlyList<OpenLibraryWork> works,
            CancellationToken cancellationToken)
        {
            if (!_geminiOptions.IsConfigured)
            {
                logger.LogInformation(
                    "Gemini API key not configured; ranking {Count} candidates deterministically.",
                    works.Count);

                return Fallback(query, works, NotConfiguredReason);
            }

            try
            {
                var ranked = await llmBookRanker.RankAsync(rawQuery, query, works, cancellationToken);

                return BookRankingResult.FromLlm(ranked);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller hung up. Ranking again would be wasted work.
                throw;
            }
            catch (LlmRankingException exception)
            {
                logger.LogWarning(
                    exception,
                    "Gemini ranking failed for {Query}; ranking deterministically.",
                    rawQuery);

                return Fallback(query, works, UnavailableReason);
            }
        }

        private BookRankingResult Fallback(
            ExtractedQuery query,
            IReadOnlyList<OpenLibraryWork> works,
            string reason) =>
            BookRankingResult.FromFallback(fallbackBookRanker.Rank(query, works), reason);
    }
}
