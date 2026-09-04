namespace FindThatBook.Api.Exceptions;

/// <summary>
/// Thrown when the language model could not interpret a query: unreachable, rejected the
/// request, timed out, or returned a body we could not read.
/// </summary>
/// <remarks>
/// This is expected to be caught by the extraction orchestrator and turned into a
/// deterministic fallback, so it normally never reaches the client. Messages may quote
/// provider responses and are therefore treated as internal detail, not caller-facing text.
/// </remarks>
public sealed class LlmExtractionException : Exception
{
    public LlmExtractionException(string message) : base(message)
    {
    }

    public LlmExtractionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
