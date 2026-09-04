namespace FindThatBook.Api.Exceptions;

/// <summary>
/// Thrown when Open Library could not be reached, rejected the request, or returned a body
/// we could not read.
/// </summary>
/// <remarks>
/// Unlike <see cref="LlmExtractionException"/> there is no fallback for this one: without
/// Open Library there are no candidates to return, so
/// <see cref="Infrastructure.GlobalExceptionHandler"/> maps it to a 502. Messages may quote
/// provider responses and are treated as internal detail, not caller-facing text.
/// </remarks>
public sealed class OpenLibraryException : Exception
{
    public OpenLibraryException(string message) : base(message)
    {
    }

    public OpenLibraryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
