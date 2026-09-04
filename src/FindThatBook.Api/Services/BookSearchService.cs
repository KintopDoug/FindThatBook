using FindThatBook.Api.Models.Response;
using FindThatBook.Api.Models.DTO;
using Microsoft.Extensions.Logging;
namespace FindThatBook.Api.Services;

public class BookSearchService : IBookSearchService
{
    private readonly ILogger _logger;
    private readonly IQueryValidationService _queryValidationService;
    private readonly IQueryExtractionService _queryExtractionService;

    public BookSearchService(
        ILogger<BookSearchService> logger,
        IQueryValidationService queryValidationService,
        IQueryExtractionService queryExtractionService)
    {
        _logger = logger;
        _queryValidationService = queryValidationService;
        _queryExtractionService = queryExtractionService;
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
        //ranking

        // Returning the interpretation for now so the transport contract can be exercised end to end.
        return new BookSearchResponse
        {
            Query = normalizedQuery,
            Interpretation = extraction.Query,
            ExtractionSource = extraction.Source,
            FallbackReason = extraction.FallbackReason
        };
    }
}
