using FindThatBook.Api.Configuration;
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
    IOptions<SearchOptions> searchOptions) : ControllerBase
{
    private readonly SearchOptions _searchOptions = searchOptions.Value;

    /// <summary>
    /// Searches for books matching a messy plain-text query
    /// </summary>
    /// <param name="query">The raw user query. Title, author, keywords, or any mix of them.</param>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Search([FromQuery] string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            logger.LogInformation("Search rejected: empty query.");
            return ValidationProblem("Query must not be empty.");
        }

        // Trim only. Deeper normalization belongs in the search pipeline, not the transport
        // layer, so the raw query stays available for the LLM extraction step.
        var trimmed = query.Trim();

        if (trimmed.Length > _searchOptions.MaxQueryLength)
        {
            logger.LogInformation(
                "Search rejected: query length {Length} exceeds {MaxQueryLength}.",
                trimmed.Length,
                _searchOptions.MaxQueryLength);

            return ValidationProblem(
                $"Query must be {_searchOptions.MaxQueryLength} characters or fewer.");
        }

        logger.LogInformation("Search accepted for query {Query}.", trimmed);

        // TODO: LLM extraction of query -> Open Library retrieval -> ranking. Returning the accepted
        // query for now so the transport contract can be exercised end to end.
        return Ok(new { query = trimmed });
    }
}
