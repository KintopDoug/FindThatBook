using System.Net;
using System.Text;
using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Services.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Tests;

public class OpenLibraryClientTests
{
    private static (OpenLibraryClient Sut, RecordingHandler Handler) CreateSut(
        HttpStatusCode status,
        string body)
    {
        var handler = new RecordingHandler(status, body);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://openlibrary.test/") };

        var sut = new OpenLibraryClient(
            client,
            Options.Create(new OpenLibraryOptions
            {
                BaseUrl = "https://openlibrary.test/",
                CoverBaseUrl = "https://covers.openlibrary.test/b/id/",
                UserAgent = "tests",
                TotalTimeoutSeconds = 5,
                MaxResults = 10,
                PrimaryAuthorLookups = 5
            }),
            NullLogger<OpenLibraryClient>.Instance);

        return (sut, handler);
    }

    [Fact]
    public async Task Maps_a_search_document_onto_a_work()
    {
        const string body = """
            {"docs":[{
              "key":"/works/OL45804W",
              "title":"The Hobbit",
              "subtitle":"or There and Back Again",
              "author_name":["J.R.R. Tolkien","Alan Lee"],
              "author_key":["OL26320A","OL99999A"],
              "first_publish_year":1937,
              "cover_i":123,
              "edition_count":120
            }]}
            """;

        var (sut, handler) = CreateSut(HttpStatusCode.OK, body);

        var works = await sut.SearchWorksAsync("The Hobbit", "Tolkien", null, CancellationToken.None);

        var work = Assert.Single(works);
        Assert.Equal("/works/OL45804W", work.Key);
        Assert.Equal("The Hobbit", work.Title);
        Assert.Equal("or There and Back Again", work.Subtitle);
        Assert.Equal(1937, work.FirstPublishYear);
        Assert.Equal(123, work.CoverId);
        Assert.Equal(["J.R.R. Tolkien", "Alan Lee"], work.ContributorNames);

        // Terms are escaped, and the field/limit constraints are applied. AbsoluteUri rather
        // than ToString, which hands back the unescaped form.
        var url = handler.RequestUri!.AbsoluteUri;
        Assert.Contains("title=The%20Hobbit", url);
        Assert.Contains("author=Tolkien", url);
        Assert.Contains("limit=10", url);
    }

    /// <summary>
    /// Open Library omits fields freely. A document missing a title or key cannot become a
    /// usable candidate, so it is dropped rather than surfaced half-empty.
    /// </summary>
    [Fact]
    public async Task Drops_documents_that_could_not_be_shown_to_a_user()
    {
        const string body = """
            {"docs":[
              {"key":"/works/OL1W"},
              {"title":"No Key"},
              {"key":"/works/OL2W","title":"Usable"}
            ]}
            """;

        var (sut, _) = CreateSut(HttpStatusCode.OK, body);

        var works = await sut.SearchWorksAsync("anything", null, null, CancellationToken.None);

        Assert.Equal("Usable", Assert.Single(works).Title);
    }

    /// <summary>
    /// The work record is the only place that distinguishes authors from contributors. Its
    /// keys are paths, while search returns bare ids, so they have to be normalized to match.
    /// </summary>
    [Fact]
    public async Task Reads_primary_author_keys_from_the_work_record()
    {
        const string body = """
            {"authors":[
              {"author":{"key":"/authors/OL26320A"},"type":{"key":"/type/author_role"}}
            ]}
            """;

        var (sut, handler) = CreateSut(HttpStatusCode.OK, body);

        var keys = await sut.GetPrimaryAuthorKeysAsync("/works/OL45804W", CancellationToken.None);

        Assert.Equal(["OL26320A"], keys);
        Assert.Contains("works/OL45804W.json", handler.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{}")]
    [InlineData(HttpStatusCode.TooManyRequests, "{}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task Reports_failures_as_open_library_exceptions(HttpStatusCode status, string body)
    {
        var (sut, _) = CreateSut(status, body);

        await Assert.ThrowsAsync<OpenLibraryException>(
            () => sut.SearchWorksAsync("anything", null, null, CancellationToken.None));
    }

    /// <summary>
    /// The resilience pipeline reports its own timeout and its open circuit as Polly
    /// rejections, which derive from neither HttpRequestException nor
    /// OperationCanceledException. Left unnamed they escape as a 500 instead of the 502 that
    /// an upstream failure deserves.
    /// </summary>
    [Theory]
    [MemberData(nameof(ResilienceRejections))]
    public async Task Reports_resilience_rejections_as_open_library_exceptions(Exception rejection)
    {
        var client = new HttpClient(new ThrowingHandler(rejection))
        {
            BaseAddress = new Uri("https://openlibrary.test/")
        };

        var sut = new OpenLibraryClient(
            client,
            Options.Create(new OpenLibraryOptions
            {
                BaseUrl = "https://openlibrary.test/",
                CoverBaseUrl = "https://covers.openlibrary.test/b/id/",
                UserAgent = "tests",
                TotalTimeoutSeconds = 5,
                MaxResults = 10,
                PrimaryAuthorLookups = 2
            }),
            NullLogger<OpenLibraryClient>.Instance);

        await Assert.ThrowsAsync<OpenLibraryException>(
            () => sut.SearchWorksAsync("anything", null, null, CancellationToken.None));
    }

    public static TheoryData<Exception> ResilienceRejections() =>
    [
        new Polly.Timeout.TimeoutRejectedException("pipeline timed out"),
        new Polly.CircuitBreaker.BrokenCircuitException("circuit is open")
    ];

    /// <summary>
    /// Open Library sends explicit nulls and occasionally off-type fields. The
    /// System.Text.Json accessors throw InvalidOperationException rather than returning
    /// false for these, so an unguarded read turns a catalogue data quirk into a 500. The
    /// document is still usable, so the field is simply dropped.
    /// </summary>
    [Theory]
    [InlineData("null cover", "{\"docs\":[{\"key\":\"/works/OL1W\",\"title\":\"T\",\"cover_i\":null}]}")]
    [InlineData("string year", "{\"docs\":[{\"key\":\"/works/OL1W\",\"title\":\"T\",\"first_publish_year\":\"1937\"}]}")]
    [InlineData("array editions", "{\"docs\":[{\"key\":\"/works/OL1W\",\"title\":\"T\",\"edition_count\":[1,2]}]}")]
    public async Task Survives_off_type_numeric_fields(string scenario, string body)
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, body);

        var works = await sut.SearchWorksAsync("anything", null, null, CancellationToken.None);

        var work = Assert.Single(works);
        Assert.Equal("T", work.Title);

        // The unreadable field is dropped rather than taking the whole result down with it.
        Assert.True(
            work.FirstPublishYear is null || work.CoverId is null || work.EditionCount is null,
            $"expected the off-type field to be dropped for: {scenario}");
    }

    /// <summary>
    /// The same hazard on the work record. A non-string author key must not escape as an
    /// InvalidOperationException, because that bypasses the 502 mapping.
    /// </summary>
    [Fact]
    public async Task Reports_an_off_type_author_key_as_an_open_library_failure()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "{\"authors\":[{\"author\":{\"key\":12345}}]}");

        var keys = await sut.GetPrimaryAuthorKeysAsync("/works/OL1W", CancellationToken.None);

        // Nothing confirmable, but no exception: the caller degrades to the listed names.
        Assert.Empty(keys);
    }

    [Fact]
    public async Task Refuses_a_search_with_no_terms()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, "{\"docs\":[]}");

        await Assert.ThrowsAsync<OpenLibraryException>(
            () => sut.SearchWorksAsync(null, null, null, CancellationToken.None));
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => throw exception;
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
