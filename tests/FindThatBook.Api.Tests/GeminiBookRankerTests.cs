using System.Net;
using System.Text;
using System.Text.Json;
using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Models.DTO;
using FindThatBook.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Tests;

public class GeminiBookRankerTests
{
    private static readonly OpenLibraryWork First = new()
    {
        Key = "/works/OL1W",
        Title = "The Hobbit",
        ContributorNames = ["J.R.R. Tolkien"],
        ContributorKeys = ["OL26320A"]
    };

    private static readonly OpenLibraryWork Second = new()
    {
        Key = "/works/OL2W",
        Title = "The Hobbit (adaptation)",
        ContributorNames = ["Someone Else"],
        ContributorKeys = ["OL9A"]
    };

    private static string Envelope(string modelJson) =>
        "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":"
        + JsonSerializer.Serialize(modelJson)
        + "}]}}]}";

    private static (GeminiBookRanker Sut, RecordingHandler Handler) CreateSut(
        HttpStatusCode status,
        string body)
    {
        var handler = new RecordingHandler(status, body);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };

        var sut = new GeminiBookRanker(
            client,
            Options.Create(new GeminiOptions
            {
                ApiKey = "secret-key",
                Model = "test-model",
                BaseUrl = "https://example.invalid/",
                TimeoutSeconds = 5
            }),
            NullLogger<GeminiBookRanker>.Instance);

        return (sut, handler);
    }

    private static Task<IReadOnlyList<RankedWork>> Rank(GeminiBookRanker sut) =>
        sut.RankAsync(
            "the hobbit by tolkien",
            new ExtractedQuery { Title = "The Hobbit", Author = "Tolkien" },
            [First, Second],
            CancellationToken.None);

    [Fact]
    public async Task Applies_the_order_the_model_returned()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, Envelope("""
            {"ranked":[
              {"key":"/works/OL2W","explanation":"Closest match on title."},
              {"key":"/works/OL1W","explanation":"Also matches the title."}
            ]}
            """));

        var ranked = await Rank(sut);

        Assert.Equal(["/works/OL2W", "/works/OL1W"], ranked.Select(r => r.Work.Key));
        Assert.Equal("Closest match on title.", ranked[0].Explanation);

        // The key travels as a header, never in the URL.
        Assert.Equal("secret-key", handler.ApiKeyHeader);
        Assert.DoesNotContain("secret-key", handler.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    /// <summary>
    /// The model must never be able to put a book in front of a user that the catalogue did
    /// not return. Keys we did not send describe nothing real.
    /// </summary>
    [Fact]
    public async Task Discards_keys_that_were_never_sent()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, Envelope("""
            {"ranked":[
              {"key":"/works/INVENTED","explanation":"A book that does not exist."},
              {"key":"/works/OL1W","explanation":"Title matches."}
            ]}
            """));

        var ranked = await Rank(sut);

        Assert.DoesNotContain(ranked, r => r.Work.Key == "/works/INVENTED");
        Assert.Equal("/works/OL1W", ranked[0].Work.Key);
    }

    /// <summary>
    /// A truncated or lazy answer should cost ordering quality, not results.
    /// </summary>
    [Fact]
    public async Task Appends_candidates_the_model_left_out()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, Envelope("""
            {"ranked":[{"key":"/works/OL2W","explanation":"Only this one."}]}
            """));

        var ranked = await Rank(sut);

        Assert.Equal(2, ranked.Count);
        Assert.Equal("/works/OL2W", ranked[0].Work.Key);
        Assert.Equal("/works/OL1W", ranked[1].Work.Key);
    }

    [Fact]
    public async Task Ignores_a_candidate_repeated_by_the_model()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, Envelope("""
            {"ranked":[
              {"key":"/works/OL1W","explanation":"First."},
              {"key":"/works/OL1W","explanation":"Again."},
              {"key":"/works/OL2W","explanation":"Second."}
            ]}
            """));

        var ranked = await Rank(sut);

        Assert.Equal(2, ranked.Count);
        Assert.Equal(["/works/OL1W", "/works/OL2W"], ranked.Select(r => r.Work.Key));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{}")]
    [InlineData(HttpStatusCode.TooManyRequests, "{}")]
    [InlineData(HttpStatusCode.OK, "not json at all")]
    public async Task Reports_unusable_answers_as_ranking_failures(HttpStatusCode status, string body)
    {
        var (sut, _) = CreateSut(status, body);

        await Assert.ThrowsAsync<LlmRankingException>(() => Rank(sut));
    }

    /// <summary>An answer with nothing usable in it is a failure, not an empty ranking.</summary>
    [Fact]
    public async Task Reports_an_answer_of_only_unknown_keys_as_a_failure()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, Envelope("""
            {"ranked":[{"key":"/works/NOPE","explanation":"Invented."}]}
            """));

        await Assert.ThrowsAsync<LlmRankingException>(() => Rank(sut));
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
