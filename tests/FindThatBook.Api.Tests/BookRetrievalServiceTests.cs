using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Models.DTO;
using FindThatBook.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Tests;

public class BookRetrievalServiceTests
{
    private static OpenLibraryOptions Options(int primaryAuthorLookups) => new()
    {
        BaseUrl = "https://example.invalid/",
        CoverBaseUrl = "https://covers.example.invalid/b/id/",
        UserAgent = "tests",
        TotalTimeoutSeconds = 5,
        MaxResults = 10,
        PrimaryAuthorLookups = primaryAuthorLookups,
        CacheLifetimeMinutes = 45,
        CacheMaxEntries = 100
    };

    private static BookRetrievalService CreateSut(
        IOpenLibraryClient client,
        int primaryAuthorLookups = 5,
        BookRetrievalCache? cache = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(Options(primaryAuthorLookups));

        return new BookRetrievalService(
            client,
            cache ?? new BookRetrievalCache(options),
            options,
            NullLogger<BookRetrievalService>.Instance);
    }

    private static OpenLibraryWork Work(
        string key = "/works/OL1W",
        string[]? names = null,
        string[]? keys = null) =>
        new()
        {
            Key = key,
            Title = "The Hobbit",
            ContributorNames = names ?? ["J.R.R. Tolkien"],
            ContributorKeys = keys ?? ["OL26320A"]
        };

    /// <summary>
    /// The search should be as specific as the interpretation allows, and the strategy has to
    /// reflect what was actually searched so explanations stay truthful.
    /// </summary>
    [Theory]
    [InlineData("The Hobbit", "Tolkien", RetrievalStrategy.TitleAndAuthor)]
    [InlineData("The Hobbit", null, RetrievalStrategy.TitleOnly)]
    [InlineData(null, "Tolkien", RetrievalStrategy.AuthorOnly)]
    [InlineData(null, null, RetrievalStrategy.Keywords)]
    public async Task Searches_as_specifically_as_the_interpretation_allows(
        string? title,
        string? author,
        RetrievalStrategy expected)
    {
        var client = new StubClient([Work()]);

        var result = await CreateSut(client, primaryAuthorLookups: 0).RetrieveAsync(
            new ExtractedQuery { Title = title, Author = author, Keywords = ["hobbit"] },
            CancellationToken.None);

        Assert.Equal(expected, result.Strategy);
        Assert.Equal(title, client.Searches[0].Title);
        Assert.Equal(author, client.Searches[0].Author);
    }

    /// <summary>
    /// A mislabelled title or author should not turn into an empty page when a general search
    /// would have found the book.
    /// </summary>
    [Fact]
    public async Task Widens_the_search_when_the_field_search_finds_nothing()
    {
        // First call returns nothing, second returns a hit.
        var client = new StubClient([], [Work()]);

        var result = await CreateSut(client, primaryAuthorLookups: 0).RetrieveAsync(
            new ExtractedQuery { Title = "Hobbit", Author = "Tolkien" },
            CancellationToken.None);

        Assert.Equal(RetrievalStrategy.WidenedAfterNoMatches, result.Strategy);
        Assert.Single(result.Works);

        // The retry drops the field constraints and searches everything as one query.
        Assert.Null(client.Searches[1].Title);
        Assert.Null(client.Searches[1].Author);
        Assert.Contains("Tolkien", client.Searches[1].GeneralTerms);
    }

    /// <summary>
    /// The data-quality case from the brief: search lists an illustrator alongside the author,
    /// and only the canonical work record can tell them apart.
    /// </summary>
    [Fact]
    public async Task Keeps_only_authors_the_work_record_confirms()
    {
        var client = new StubClient(
            [Work(names: ["J.R.R. Tolkien", "Alan Lee"], keys: ["OL26320A", "OL99999A"])])
        {
            PrimaryAuthorKeys = ["OL26320A"]   // the illustrator is not an author
        };

        var result = await CreateSut(client).RetrieveAsync(
            new ExtractedQuery { Title = "The Hobbit" },
            CancellationToken.None);

        var work = Assert.Single(result.Works);
        Assert.Equal(["J.R.R. Tolkien"], work.PrimaryAuthorNames);
        Assert.Equal(["J.R.R. Tolkien"], work.BestKnownAuthors);
        Assert.True(work.HasConfirmedPrimaryAuthors);
    }

    /// <summary>
    /// Confirming authors is an improvement, not a requirement. Losing that one call must not
    /// lose the search.
    /// </summary>
    [Fact]
    public async Task Falls_back_to_listed_names_when_confirmation_fails()
    {
        var client = new StubClient([Work()]) { ThrowOnWorkLookup = true };

        var result = await CreateSut(client).RetrieveAsync(
            new ExtractedQuery { Title = "The Hobbit" },
            CancellationToken.None);

        var work = Assert.Single(result.Works);
        Assert.False(work.HasConfirmedPrimaryAuthors);
        Assert.Equal(["J.R.R. Tolkien"], work.BestKnownAuthors);
    }

    [Fact]
    public async Task Returns_nothing_without_calling_open_library_when_there_is_no_interpretation()
    {
        var client = new StubClient([Work()]);

        var result = await CreateSut(client).RetrieveAsync(new ExtractedQuery(), CancellationToken.None);

        Assert.Empty(result.Works);
        Assert.Empty(client.Searches);
    }

    /// <summary>
    /// The point of the cache: a repeat of the same interpretation must cost no Open Library
    /// requests at all, since one search spends several against a per-second budget.
    /// </summary>
    [Fact]
    public async Task Repeat_searches_do_not_reach_open_library()
    {
        var client = new StubClient([Work()], [Work()]);
        var cache = new BookRetrievalCache(
            Microsoft.Extensions.Options.Options.Create(Options(primaryAuthorLookups: 0)));

        var query = new ExtractedQuery { Title = "The Hobbit", Author = "Tolkien" };

        var first = await CreateSut(client, 0, cache).RetrieveAsync(query, CancellationToken.None);
        Assert.Single(client.Searches);

        // A separate service instance, as a second request would use.
        var second = await CreateSut(client, 0, cache).RetrieveAsync(
            new ExtractedQuery { Title = "the hobbit", Author = "tolkien" },
            CancellationToken.None);

        Assert.Single(client.Searches);                       // still one: nothing new was fetched
        Assert.Equal(first.Strategy, second.Strategy);
        Assert.Equal(first.Works.Count, second.Works.Count);
    }

    /// <summary>
    /// An empty result is worth caching too: it is exactly the query a user retypes, and
    /// asking again cannot produce a different answer within the cache lifetime.
    /// </summary>
    [Fact]
    public async Task Repeat_searches_that_found_nothing_also_stay_cached()
    {
        var client = new StubClient([], [], []);
        var cache = new BookRetrievalCache(
            Microsoft.Extensions.Options.Options.Create(Options(primaryAuthorLookups: 0)));

        var query = new ExtractedQuery { Keywords = ["nothingmatchesthis"] };

        await CreateSut(client, 0, cache).RetrieveAsync(query, CancellationToken.None);
        var callsAfterFirst = client.Searches.Count;

        await CreateSut(client, 0, cache).RetrieveAsync(query, CancellationToken.None);

        Assert.Equal(callsAfterFirst, client.Searches.Count);
    }

    /// <summary>A failed search must not be cached, or one outage poisons the next 45 minutes.</summary>
    [Fact]
    public async Task Failures_are_not_cached()
    {
        var cache = new BookRetrievalCache(
            Microsoft.Extensions.Options.Options.Create(Options(primaryAuthorLookups: 0)));

        var failing = new ThrowingSearchClient();
        var query = new ExtractedQuery { Title = "The Hobbit" };

        await Assert.ThrowsAsync<OpenLibraryException>(
            () => CreateSut(failing, 0, cache).RetrieveAsync(query, CancellationToken.None));

        // The next caller gets a real attempt rather than a cached failure.
        var recovered = new StubClient([Work()]);
        var result = await CreateSut(recovered, 0, cache).RetrieveAsync(query, CancellationToken.None);

        Assert.Single(result.Works);
        Assert.Single(recovered.Searches);
    }

    private sealed class ThrowingSearchClient : IOpenLibraryClient
    {
        public Task<IReadOnlyList<OpenLibraryWork>> SearchWorksAsync(
            string? title, string? author, string? generalTerms, CancellationToken cancellationToken) =>
            throw new OpenLibraryException("Open Library returned 503.");

        public Task<IReadOnlyList<string>> GetPrimaryAuthorKeysAsync(
            string workKey, CancellationToken cancellationToken) =>
            throw new OpenLibraryException("Open Library returned 503.");
    }

    [Fact]
    public async Task Removes_works_repeated_across_pages()
    {
        var client = new StubClient([Work(key: "/works/OL1W"), Work(key: "/works/OL1W")]);

        var result = await CreateSut(client, primaryAuthorLookups: 0).RetrieveAsync(
            new ExtractedQuery { Title = "The Hobbit" },
            CancellationToken.None);

        Assert.Single(result.Works);
    }

    private sealed class StubClient(params IReadOnlyList<OpenLibraryWork>[] responses) : IOpenLibraryClient
    {
        private int _call;

        public List<(string? Title, string? Author, string? GeneralTerms)> Searches { get; } = [];

        public IReadOnlyList<string> PrimaryAuthorKeys { get; init; } = [];

        public bool ThrowOnWorkLookup { get; init; }

        public Task<IReadOnlyList<OpenLibraryWork>> SearchWorksAsync(
            string? title,
            string? author,
            string? generalTerms,
            CancellationToken cancellationToken)
        {
            Searches.Add((title, author, generalTerms));

            var response = _call < responses.Length ? responses[_call] : [];
            _call++;

            return Task.FromResult(response);
        }

        public Task<IReadOnlyList<string>> GetPrimaryAuthorKeysAsync(
            string workKey,
            CancellationToken cancellationToken)
        {
            if (ThrowOnWorkLookup)
            {
                throw new OpenLibraryException("Work record unavailable.");
            }

            return Task.FromResult(PrimaryAuthorKeys);
        }
    }
}
