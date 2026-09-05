using System.Text.Json;
using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Models.DTO;
using Microsoft.Extensions.Options;
using Polly;

namespace FindThatBook.Api.Services.Retrieval
{
    /// <summary>
    /// Thin HTTP wrapper over the Open Library endpoints we use. Knows the wire format and
    /// nothing about matching.
    /// </summary>
    public class OpenLibraryClient(
        HttpClient httpClient,
        IOptions<OpenLibraryOptions> openLibraryOptions,
        ILogger<OpenLibraryClient> logger) : IOpenLibraryClient
    {
        private readonly OpenLibraryOptions _options = openLibraryOptions.Value;

        /// <summary>
        /// Restricting the returned fields keeps responses small; the default document is
        /// very large and we use almost none of it.
        /// </summary>
        private const string Fields =
            "key,title,subtitle,author_name,author_key,first_publish_year,cover_i,edition_count";

        public async Task<IReadOnlyList<OpenLibraryWork>> SearchWorksAsync(
            string? title,
            string? author,
            string? generalTerms,
            CancellationToken cancellationToken)
        {
            var parameters = new List<string>();

            if (!string.IsNullOrWhiteSpace(title))
                parameters.Add($"title={Uri.EscapeDataString(title)}");

            if (!string.IsNullOrWhiteSpace(author))
                parameters.Add($"author={Uri.EscapeDataString(author)}");

            if (!string.IsNullOrWhiteSpace(generalTerms))
                parameters.Add($"q={Uri.EscapeDataString(generalTerms)}");

            if (parameters.Count == 0)
            {
                throw new OpenLibraryException("A search needs at least one term.");
            }

            parameters.Add($"fields={Fields}");
            parameters.Add($"limit={_options.MaxResults}");

            var payload = await GetAsync($"search.json?{string.Join('&', parameters)}", cancellationToken);

            return ParseSearchResults(payload);
        }

        public async Task<IReadOnlyList<string>> GetPrimaryAuthorKeysAsync(
            string workKey,
            CancellationToken cancellationToken)
        {
            // Search returns "/works/OL45804W"; the work endpoint wants "works/OL45804W.json".
            var payload = await GetAsync($"{workKey.TrimStart('/')}.json", cancellationToken);

            try
            {
                using var document = JsonDocument.Parse(payload);

                if (!document.RootElement.TryGetProperty("authors", out var authors)
                    || authors.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }

                return authors
                    .EnumerateArray()
                    .Select(entry => entry.TryGetProperty("author", out var author)
                        && author.TryGetProperty("key", out var key)
                            ? key.GetString()
                            : null)
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Select(key => NormalizeAuthorKey(key!))
                    .ToArray();
            }
            catch (JsonException exception)
            {
                throw new OpenLibraryException($"Work record {workKey} was not readable.", exception);
            }
        }

        /// <summary>Search returns bare ids ("OL26320A"); work records return paths.</summary>
        internal static string NormalizeAuthorKey(string key) =>
            key.TrimStart('/').StartsWith("authors/", StringComparison.OrdinalIgnoreCase)
                ? key.TrimStart('/')["authors/".Length..]
                : key.TrimStart('/');

        private async Task<string> GetAsync(string relativeUrl, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TotalTimeoutSeconds));

            HttpResponseMessage response;
            try
            {
                response = await httpClient.GetAsync(relativeUrl, timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller gave up; that is not an Open Library failure.
                throw;
            }
            catch (ExecutionRejectedException exception)
            {
                // The resilience pipeline gave up: its own timeout expired, or the circuit is
                // open after repeated failures. These derive from neither HttpRequestException
                // nor OperationCanceledException, so without this they would surface as a 500.
                throw new OpenLibraryException("Open Library was unreachable or unresponsive.", exception);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                throw new OpenLibraryException("Open Library request failed.", exception);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Open Library returned {StatusCode} for {Url}.",
                        (int)response.StatusCode,
                        relativeUrl);

                    throw new OpenLibraryException($"Open Library returned {(int)response.StatusCode}.");
                }

                return await response.Content.ReadAsStringAsync(timeout.Token);
            }
        }

        private static IReadOnlyList<OpenLibraryWork> ParseSearchResults(string payload)
        {
            try
            {
                using var document = JsonDocument.Parse(payload);

                if (!document.RootElement.TryGetProperty("docs", out var docs)
                    || docs.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }

                return docs
                    .EnumerateArray()
                    .Select(ToWork)
                    .Where(work => work is not null)
                    .Select(work => work!)
                    .ToArray();
            }
            catch (JsonException exception)
            {
                throw new OpenLibraryException("Open Library search response was not readable.", exception);
            }
        }

        /// <summary>
        /// Maps one search document. Returns null for documents missing the fields that make
        /// a result usable, rather than surfacing a candidate with no title or no identity.
        /// </summary>
        private static OpenLibraryWork? ToWork(JsonElement document)
        {
            var key = ReadString(document, "key");
            var title = ReadString(document, "title");

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            return new OpenLibraryWork
            {
                Key = key,
                Title = title,
                Subtitle = ReadString(document, "subtitle"),
                ContributorNames = ReadStringArray(document, "author_name"),
                ContributorKeys = ReadStringArray(document, "author_key")
                    .Select(NormalizeAuthorKey)
                    .ToArray(),
                FirstPublishYear = ReadInt(document, "first_publish_year"),
                CoverId = ReadInt(document, "cover_i"),
                EditionCount = ReadInt(document, "edition_count")
            };
        }

        private static string? ReadString(JsonElement document, string property) =>
            document.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static int? ReadInt(JsonElement document, string property) =>
            document.TryGetProperty(property, out var value) && value.TryGetInt32(out var number)
                ? number
                : null;

        private static IReadOnlyList<string> ReadStringArray(JsonElement document, string property)
        {
            if (!document.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return value
                .EnumerateArray()
                .Where(entry => entry.ValueKind == JsonValueKind.String)
                .Select(entry => entry.GetString()!)
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .ToArray();
        }
    }
}
