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

    public BookSearchService(
        ILogger<BookSearchService> logger,
        IQueryValidationService queryValidationService,
        IQueryExtractionService queryExtractionService,
        IBookRetrievalService bookRetrievalService,
        IBookRankingService bookRankingService,
        IBookCandidateMapper bookCandidateMapper)
    {
        _logger = logger;
        _queryValidationService = queryValidationService;
        _queryExtractionService = queryExtractionService;
        _bookRetrievalService = bookRetrievalService;
        _bookRankingService = bookRankingService;
        _bookCandidateMapper = bookCandidateMapper;
    }

    public async Task<BookSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        //sanitize and validate query. throws if invalid and handled by global exception handler
        string? normalizedQuery = _queryValidationService.NormalizeAndValidate(query);

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
            return new BookSearchResponse
            {
                Query = normalizedQuery,
                Interpretation = extraction.Query,
                ExtractionSource = extraction.Source,
                ExtractionFallbackReason = extraction.FallbackReason
            };
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

        return new BookSearchResponse
        {
            Query = normalizedQuery,
            Interpretation = extraction.Query,
            ExtractionSource = extraction.Source,
            ExtractionFallbackReason = extraction.FallbackReason,
            RankingSource = ranking.Source,
            RankingFallbackReason = ranking.FallbackReason,
            Results = _bookCandidateMapper.ToCandidates(ranking)
        };
    }
}
