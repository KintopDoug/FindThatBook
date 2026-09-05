using System.Net;
using System.Text;
using System.Text.Json;
using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Services.Extraction;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Tests;

public class GeminiQueryExtractorTests
{
    /// <summary>Wraps model output in the envelope Gemini actually returns.</summary>
    private static string Envelope(string modelJson) =>
        "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":"
        + JsonSerializer.Serialize(modelJson)
        + "}]}}]}";

    private static (GeminiQueryExtractor Sut, RecordingHandler Handler) CreateSut(
        HttpStatusCode status,
        string body)
    {
        var handler = new RecordingHandler(status, body);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };

        var sut = new GeminiQueryExtractor(
            client,
            Options.Create(new GeminiOptions
            {
                ApiKey = "secret-key",
                Model = "test-model",
                BaseUrl = "https://example.invalid/",
                TimeoutSeconds = 5
            }),
            NullLogger<GeminiQueryExtractor>.Instance);

        return (sut, handler);
    }

    [Fact]
    public async Task Reads_the_model_answer_out_of_the_response_envelope()
    {
        var (sut, handler) = CreateSut(
            HttpStatusCode.OK,
            Envelope("""{"title":"The Hobbit","author":"J.R.R. Tolkien","keywords":["fantasy","fantasy"]}"""));

        var result = await sut.ExtractAsync("that hobbit book by tolkein", CancellationToken.None);

        Assert.Equal("The Hobbit", result.Title);
        Assert.Equal("J.R.R. Tolkien", result.Author);
        Assert.Equal(["fantasy"], result.Keywords);   // duplicates collapsed

        // The key must travel as a header. In the query string it would leak into request
        // logs, proxy traces, and browser history.
        Assert.Equal("secret-key", handler.ApiKeyHeader);
        Assert.DoesNotContain("secret-key", handler.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every shape of bad answer becomes one exception type, so the orchestrator has a
    /// single thing to catch when deciding to fall back.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{}")]                     // provider down
    [InlineData(HttpStatusCode.TooManyRequests, "{}")]                        // rate limited
    [InlineData(HttpStatusCode.OK, "{\"candidates\":[]}")]                    // envelope missing content
    [InlineData(HttpStatusCode.OK, "not json at all")]                        // envelope unparseable
    public async Task Reports_unusable_answers_as_extraction_failures(HttpStatusCode status, string body)
    {
        var (sut, _) = CreateSut(status, body);

        await Assert.ThrowsAsync<LlmExtractionException>(
            () => sut.ExtractAsync("anything", CancellationToken.None));
    }

    [Fact]
    public async Task Reports_model_content_that_is_not_json_as_a_failure()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, Envelope("I could not determine a title."));

        await Assert.ThrowsAsync<LlmExtractionException>(
            () => sut.ExtractAsync("anything", CancellationToken.None));
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? ApiKeyHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKeyHeader = request.Headers.TryGetValues("x-goog-api-key", out var values)
                ? values.FirstOrDefault()
                : null;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
