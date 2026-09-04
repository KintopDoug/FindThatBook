namespace FindThatBook.Api.Exceptions;

/// <summary>
/// Thrown when a user query fails validation and cannot be searched.
/// </summary>
/// <remarks>
/// This is caller error, not a server fault, so
/// <see cref="FindThatBook.Api.Infrastructure.GlobalExceptionHandler"/> maps it to a 400.
/// Messages describe the caller's own input and expose nothing internal, which is
/// why they are safe to return verbatim in every environment. Keep it that way: never put a
/// provider response, file path, or configuration value in one.
/// </remarks>
public sealed class InvalidQueryException : Exception
{
    public InvalidQueryException(string message) : base(message)
    {
    }

    public InvalidQueryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The query was null, empty, or entirely whitespace.</summary>
    public static InvalidQueryException Empty() =>
        new("Query must not be empty.");

    /// <summary>The query exceeded the configured maximum length.</summary>
    public static InvalidQueryException TooLong(int maxQueryLength) =>
        new($"Query must be {maxQueryLength} characters or fewer.");

    /// <summary>The query was not valid text, for example an unpaired surrogate.</summary>
    public static InvalidQueryException Malformed() =>
        new("Query contains invalid text.");
}
