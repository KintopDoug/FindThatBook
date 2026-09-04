using Microsoft.AspNetCore.Mvc;

namespace FindThatBook.Api.Controllers;

/// <summary>
/// Scaffolding endpoint used to confirm the API is reachable and that logs written through
/// the injected <see cref="ILogger{TCategoryName}"/> reach the Aspire dashboard.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DiagnosticsController(ILogger<DiagnosticsController> logger) : ControllerBase
{
    /// <summary>Returns a simple liveness payload and writes a structured log entry.</summary>
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        // Structured (not interpolated) so the dashboard can index Service and Timestamp
        // as filterable attributes rather than baking them into the message text.
        logger.LogInformation(
            "Ping received by {Service} at {Timestamp:o}",
            "FindThatBook.Api",
            DateTimeOffset.UtcNow);

        return Ok(new
        {
            status = "ok",
            service = "FindThatBook.Api",
            utc = DateTimeOffset.UtcNow
        });
    }
}
