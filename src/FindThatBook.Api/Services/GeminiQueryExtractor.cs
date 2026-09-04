using System.Net.Http.Json;
using System.Text.Json;
using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Models.DTO;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Services
{
    /// <summary>
    /// Calls Gemini's generateContent endpoint to split a messy query into title, author,
    /// and keywords.
    /// </summary>
    /// <remarks>
    /// What the model is trusted with: deciding which words of free text are a title, which
    /// are an author, and which are neither. That judgement is genuinely linguistic and is
    /// where an LLM earns its place. Everything with a right answer stays deterministic --
    /// normalization, validation, building the Open Library request, and ranking results --
    /// so the model can never invent a book, only describe what the user asked for.
    /// </remarks>
    public class GeminiQueryExtractor(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        ILogger<GeminiQueryExtractor> logger) : ILlmQueryExtractor
    {
        private readonly GeminiOptions _options = geminiOptions.Value;

        private static readonly JsonSerializerOptions ResponseJson = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private const string SystemInstruction =
            """
            You extract search fields from a library patron's free-text request.

            Return only what the text actually supports:
            - title: the book title, if one is named. Omit subtitles and quotation marks.
            - author: the author's name, if one is named. Correct obvious misspellings of
              well-known authors, but never invent a name that is not implied by the text.
            - keywords: remaining meaningful terms such as subject, genre, or setting.

            Rules:
            - A single bare word is usually an author surname, not a title.
            - If you cannot tell whether text is a title or an author, leave both empty and
              put the words in keywords.
            - Never guess a specific book that the text does not name.
            - Omit a field entirely rather than returning an empty string.
            """;

        /// <summary>
        /// Response schema handed to Gemini so the reply is parseable JSON rather than prose.
        /// </summary>
        private static object ResponseSchema() => new
        {
            type = "OBJECT",
            properties = new
            {
                title = new { type = "STRING" },
                author = new { type = "STRING" },
                keywords = new { type = "ARRAY", items = new { type = "STRING" } }
            }
        };

        public async Task<ExtractedQuery> ExtractAsync(string query, CancellationToken cancellationToken)
        {
            var request = new
            {
                systemInstruction = new { parts = new[] { new { text = SystemInstruction } } },
                contents = new[] { new { parts = new[] { new { text = query } } } },
                generationConfig = new
                {
                    // Deterministic: the same query should interpret the same way every time.
                    temperature = 0,
                    responseMimeType = "application/json",
                    responseSchema = ResponseSchema()
                }
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            HttpResponseMessage response;
            try
            {
                // The key travels as a header, never as a query-string parameter, so it does
                // not end up in request logs or proxy traces.
                using var message = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"models/{_options.Model}:generateContent")
                {
                    Content = JsonContent.Create(request)
                };
                message.Headers.Add("x-goog-api-key", _options.ApiKey);

                response = await httpClient.SendAsync(message, timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller gave up. Let that propagate rather than reporting a model failure.
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                throw new LlmExtractionException("Gemini request failed.", exception);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

                    logger.LogWarning(
                        "Gemini returned {StatusCode} for extraction. Body: {Body}",
                        (int)response.StatusCode,
                        Truncate(body));

                    throw new LlmExtractionException(
                        $"Gemini returned {(int)response.StatusCode}.");
                }

                var payload = await response.Content.ReadAsStringAsync(timeout.Token);
                return Parse(payload);
            }
        }

        /// <summary>
        /// Pulls the model's JSON out of Gemini's response envelope and maps it onto
        /// <see cref="ExtractedQuery"/>.
        /// </summary>
        private static ExtractedQuery Parse(string payload)
        {
            string text;
            try
            {
                using var document = JsonDocument.Parse(payload);

                text = document.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString() ?? string.Empty;
            }
            catch (Exception exception)
                when (exception is JsonException or KeyNotFoundException
                    or InvalidOperationException or IndexOutOfRangeException)
            {
                throw new LlmExtractionException("Gemini response envelope was not readable.", exception);
            }

            ExtractedQueryPayload? extracted;
            try
            {
                extracted = JsonSerializer.Deserialize<ExtractedQueryPayload>(text, ResponseJson);
            }
            catch (JsonException exception)
            {
                throw new LlmExtractionException("Gemini returned content that was not valid JSON.", exception);
            }

            if (extracted is null)
            {
                throw new LlmExtractionException("Gemini returned an empty interpretation.");
            }

            return new ExtractedQuery
            {
                Title = Clean(extracted.Title),
                Author = Clean(extracted.Author),
                Keywords = extracted.Keywords?
                    .Select(Clean)
                    .Where(keyword => keyword is not null)
                    .Select(keyword => keyword!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? []
            };
        }

        /// <summary>Models sometimes return empty strings where the instruction said to omit.</summary>
        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string Truncate(string value) =>
            value.Length <= 500 ? value : value[..500] + "...";

        /// <summary>Shape of the JSON the model is asked to produce.</summary>
        private sealed record ExtractedQueryPayload(string? Title, string? Author, string[]? Keywords);
    }
}
