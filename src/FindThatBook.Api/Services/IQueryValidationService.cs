namespace FindThatBook.Api.Services
{
    public interface IQueryValidationService
    {
        /// <summary>
        /// Normalizes a raw user query and validates the result, returning the normalized
        /// text that every later stage should work from.
        /// </summary>
        /// <param name="query">The raw query as received from the caller. May be null.</param>
        /// <returns>The normalized query, guaranteed non-empty and within the configured length.</returns>
        /// <exception cref="Exceptions.InvalidQueryException">
        /// The query is empty once normalized, or exceeds the configured maximum length.
        /// </exception>
        string NormalizeAndValidate(string? query);
    }
}
