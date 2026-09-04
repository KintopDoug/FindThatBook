using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Models.Response;
using Microsoft.Extensions.Logging;
namespace FindThatBook.Api.Services;

public class BookSearchService : IBookSearchService
{
    private readonly ILogger _logger;
    private readonly IQueryValidationService _queryValidationService;

    public BookSearchService(ILogger<BookSearchService> logger, IQueryValidationService queryValidationService)
    {
        _logger = logger;
        _queryValidationService = queryValidationService;
    }

    public async Task<BookSearchResponse> SearchAsync(string query)
    {
        //sanitize and validate query
        var normalizedQuery = _queryValidationService.NormalizeAndValidate(query);

        //LLM extraction of query
        //Open Library Retrieval
        //ranking



        // Returning the accepted query for now so the transport contract can be exercised end to end.
        return new BookSearchResponse { Query = normalizedQuery };
    }
}
