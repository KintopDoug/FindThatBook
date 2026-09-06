using FindThatBook.Api.Models.Response;
using FindThatBook.Api.Services.Extraction;
using FindThatBook.Api.Services.Mapping;
using FindThatBook.Api.Services.Ranking;
using FindThatBook.Api.Services.Retrieval;
using FindThatBook.Api.Services.Validation;
using FindThatBook.Api.Models.DTO;
using Microsoft.Extensions.Logging;
namespace FindThatBook.Api.Services;

public class BookSearchService : IBookSearchService
{
    private readonly ILogger _logger;
    private readonly IQueryValidationService _queryValidationService;
    private readonly IQueryExtractionService _queryExtractionService;
    private readonly IBookRetrievalService _bookRetrievalService;
    private readonly IBookRankingService _bookRankingService;
    private readonly IBookCandidateMapper _bookCandidateMapper;
    private readonly ISearchResponseCache _searchResponseCache;

    public BookSearchService(
        ILogger<BookSearchService> logger,
        IQueryValidationService queryValidationService,
        IQueryExtractionService queryExtractionService,
        IBookRetrievalService bookRetrievalService,
        IBookRankingService bookRankingService,
        IBookCandidateMapper bookCandidateMapper,
        ISearchResponseCache searchResponseCache)
    {
        _logger = logger;
        _queryValidationService = queryValidationService;
        _queryExtractionService = queryExtractionService;
        _bookRetrievalService = bookRetrievalService;
        _bookRankingService = bookRankingService;
        _bookCandidateMapper = bookCandidateMapper;
        _searchResponseCache = searchResponseCache;
    }

    public async Task<BookSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        //sanitize and validate query. throws if invalid and handled by global exception handler
        string? normalizedQuery = _queryValidationService.NormalizeAndValidate(query);

        // Before anything expensive. The interpretation and ranking below are model calls, so
        // a hit here is the only cache that can save them -- the retrieval cache sits behind
        // extraction and cannot.
        if (_searchResponseCache.TryGet(normalizedQuery, out var cached) && cached is not null)
        {
            _logger.LogInformation(
                "Serving a cached response for {Query}; no model or catalogue calls made.",
                normalizedQuery);

            // Echo back the caller's own normalized query: the key is case-folded, so the
            // cached response may carry a differently capitalised one.
            return WithQuery(cached, normalizedQuery);
        }

        //LLM extraction of query
        QueryExtractionResult extraction = await _queryExtractionService.ExtractAsync(normalizedQuery, cancellationToken);

        _logger.LogInformation(
            "Interpreted {Query} via {Source} as title={Title} author={Author} keywords={KeywordCount}.",
            normalizedQuery,
            extraction.Source,
            extraction.Query.Title,
            extraction.Query.Author,
            extraction.Query.Keywords.Count);

        //Open Library Retrieval
        BookRetrievalResult retrieval = await _bookRetrievalService.RetrieveAsync(
            extraction.Query,
            cancellationToken);

        _logger.LogInformation(
            "Retrieved {Count} candidates for {Query} using {Strategy}.",
            retrieval.Works.Count,
            normalizedQuery,
            retrieval.Strategy);

        //ranking
        // Nothing to order means no ranking ran, so the response reports no ranking source
        // rather than naming a path that never executed.
        if (retrieval.Works.Count == 0)
        {
            return Remember(normalizedQuery, new BookSearchResponse
            {
                Query = normalizedQuery,
                Interpretation = extraction.Query,
                ExtractionSource = extraction.Source,
                ExtractionFallbackReason = extraction.FallbackReason
            },
            degraded: extraction.AiUnavailable);
        }

        BookRankingResult ranking = await _bookRankingService.RankAsync(
            normalizedQuery,
            extraction.Query,
            retrieval.Works,
            cancellationToken);

        _logger.LogInformation(
            "Ranked {Count} candidates for {Query} via {Source}.",
            ranking.Ranked.Count,
            normalizedQuery,
            ranking.Source);

        return Remember(normalizedQuery, new BookSearchResponse
        {
            Query = normalizedQuery,
            Interpretation = extraction.Query,
            ExtractionSource = extraction.Source,
            ExtractionFallbackReason = extraction.FallbackReason,
            RankingSource = ranking.Source,
            RankingFallbackReason = ranking.FallbackReason,
            Results = _bookCandidateMapper.ToCandidates(ranking)
        },
        degraded: extraction.AiUnavailable || ranking.AiUnavailable);
    }

    /// <summary>
    /// Caches a finished response unless it was degraded by an unreachable model.
    /// </summary>
    /// <remarks>
    /// Caching a degraded answer would pin the weaker result for the full cache lifetime,
    /// long after the model recovered. A search that fell back only because no key is
    /// configured is not degraded in this sense: asking again would produce the same answer,
    /// so it is worth caching.
    /// </remarks>
    private BookSearchResponse Remember(string normalizedQuery, BookSearchResponse response, bool degraded)
    {
        if (degraded)
        {
            _logger.LogInformation(
                "Not caching the response for {Query}: the model was unavailable and a retry may do better.",
                normalizedQuery);

            return response;
        }

        _searchResponseCache.Set(normalizedQuery, response);

        return response;
    }

    /// <summary>Returns the response with the caller's own spelling of the query.</summary>
    private static BookSearchResponse WithQuery(BookSearchResponse response, string query) =>
        new()
        {
            Query = query,
            Interpretation = response.Interpretation,
            ExtractionSource = response.ExtractionSource,
            ExtractionFallbackReason = response.ExtractionFallbackReason,
            RankingSource = response.RankingSource,
            RankingFallbackReason = response.RankingFallbackReason,
            Results = response.Results
        };
}
