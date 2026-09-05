using FindThatBook.Api.Models.DTO;

namespace FindThatBook.Api.Services.Retrieval
{
    public interface IOpenLibraryClient
    {
        /// <summary>
        /// Runs a works search. At least one of <paramref name="title"/>,
        /// <paramref name="author"/>, or <paramref name="generalTerms"/> must be supplied.
        /// </summary>
        /// <exception cref="Exceptions.OpenLibraryException">Open Library failed or replied unusably.</exception>
        Task<IReadOnlyList<OpenLibraryWork>> SearchWorksAsync(
            string? title,
            string? author,
            string? generalTerms,
            CancellationToken cancellationToken);

        /// <summary>
        /// Reads the canonical work record and returns the author keys it lists as authors,
        /// which excludes contributors such as illustrators and editors.
        /// </summary>
        /// <param name="workKey">Work key, for example "/works/OL45804W".</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="Exceptions.OpenLibraryException">Open Library failed or replied unusably.</exception>
        Task<IReadOnlyList<string>> GetPrimaryAuthorKeysAsync(
            string workKey,
            CancellationToken cancellationToken);
    }
}
