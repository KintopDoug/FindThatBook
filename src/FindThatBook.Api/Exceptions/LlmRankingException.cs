namespace FindThatBook.Api.Exceptions;

/// <summary>
/// Thrown when the language model could not rank a candidate list: unreachable, rejected the
/// request, timed out, or returned a body we could not read.
/// </summary>
/// <remarks>
/// Expected to be caught by the ranking orchestrator and turned into deterministic ranking,
/// so it normally never reaches the client. Messages may quote provider responses and are
/// treated as internal detail, not caller-facing text.
/// </remarks>
public sealed class LlmRankingException : Exception
{
    public LlmRankingException(string message) : base(message)
    {
    }

    public LlmRankingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
