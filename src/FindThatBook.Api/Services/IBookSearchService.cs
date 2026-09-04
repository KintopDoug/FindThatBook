using FindThatBook.Api.Models.Response;

namespace FindThatBook.Api.Services
{
    public interface IBookSearchService
    {
        Task<BookSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default);
    }
}
