using FindThatBook.Api.Configuration;
using FindThatBook.Api.Models.Response;
using FindThatBook.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Controllers;

/// <summary>
/// Entry point for the book discovery workflow. Accepts a raw, unstructured user query
/// and (once the pipeline is in place) returns ranked Open Library candidates.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SearchController(
    ILogger<SearchController> logger,
    IOptions<SearchOptions> searchOptions,
    IBookSearchService bookSearchService) : ControllerBase
{
    private readonly SearchOptions _searchOptions = searchOptions.Value;

    /// <summary>
    /// Searches for books matching a messy plain-text query
    /// </summary>
    /// <param name="query">The raw user query. Title, author, keywords, or any mix of them.</param>
    [HttpGet]
    [ProducesResponseType<BookSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<BookSearchResponse>> Search([FromQuery] string? query)
    {
        BookSearchResponse response = await bookSearchService.SearchAsync(query ?? string.Empty);

        return Ok(response);
    }
}
