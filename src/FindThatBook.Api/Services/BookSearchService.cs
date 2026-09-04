using FindThatBook.Api.Models.Response;
using Microsoft.Extensions.Logging;
namespace FindThatBook.Api.Services;

public class BookSearchService : IBookSearchService
{
    private readonly ILogger _logger;

    public BookSearchService(ILogger<BookSearchService> logger)
    {
        _logger = logger;
    }

    public async Task<BookSearchResponse> SearchAsync(string query)
    {
        //validate query
        //sanitize query
        //LLM extraction of query
        //Open Library Retrieval
        //ranking


        
        // Returning the accepted query for now so the transport contract can be exercised end to end.
        return new BookSearchResponse { Query = query };
    }
}
