using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Models.DTO;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Services.Extraction
{
    /// <summary>
    /// Chooses how a query gets interpreted: the language model when it is configured and
    /// healthy, the deterministic parser otherwise.
    /// </summary>
    /// <remarks>
    /// Degrading is never silent. The chosen path and the reason travel back on the result
    /// so the API can tell the client that AI interpretation was unavailable, rather than
    /// quietly returning weaker matches that look the same as good ones.
    /// </remarks>
    public class QueryExtractionService(
        ILlmQueryExtractor llmQueryExtractor,
        IFallbackQueryExtractor fallbackQueryExtractor,
        IOptions<GeminiOptions> geminiOptions,
        ILogger<QueryExtractionService> logger) : IQueryExtractionService
    {
        private const string NotConfiguredReason =
            "AI interpretation is not configured, so the query was parsed with built-in rules.";

        private const string UnavailableReason =
            "AI interpretation was unavailable, so the query was parsed with built-in rules.";

        private readonly GeminiOptions _geminiOptions = geminiOptions.Value;

        public async Task<QueryExtractionResult> ExtractAsync(
            string query,
            CancellationToken cancellationToken)
        {
            if (!_geminiOptions.IsConfigured)
            {
                logger.LogInformation(
                    "Gemini API key not configured; using deterministic extraction for {Query}.",
                    query);

                return Fallback(query, NotConfiguredReason);
            }

            try
            {
                var extracted = await llmQueryExtractor.ExtractAsync(query, cancellationToken);

                // A model that returns nothing usable is no better than no model at all.
                if (extracted.IsEmpty)
                {
                    logger.LogWarning(
                        "Gemini returned an empty interpretation for {Query}; using deterministic extraction.",
                        query);

                    return Fallback(query, UnavailableReason);
                }

                return QueryExtractionResult.FromLlm(extracted);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller hung up. Falling back would be wasted work.
                throw;
            }
            catch (LlmExtractionException exception)
            {
                logger.LogWarning(
                    exception,
                    "Gemini extraction failed for {Query}; using deterministic extraction.",
                    query);

                return Fallback(query, UnavailableReason);
            }
        }

        private QueryExtractionResult Fallback(string query, string reason) =>
            QueryExtractionResult.FromFallback(
                fallbackQueryExtractor.Extract(query),
                reason,
                // "Unavailable" is temporary; "not configured" is the steady state. Only the
                // former makes the result unsafe to cache.
                aiUnavailable: reason == UnavailableReason);
    }
}
