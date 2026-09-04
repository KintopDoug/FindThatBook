using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Models.DTO;
using FindThatBook.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Tests;

public class BookRetrievalServiceTests
{
    private static BookRetrievalService CreateSut(IOpenLibraryClient client, int primaryAuthorLookups = 5) =>
        new(client,
            Options.Create(new OpenLibraryOptions
            {
                BaseUrl = "https://example.invalid/",
                CoverBaseUrl = "https://covers.example.invalid/b/id/",
                UserAgent = "tests",
                TotalTimeoutSeconds = 5,
                MaxResults = 10,
                PrimaryAuthorLookups = primaryAuthorLookups
            }),
            NullLogger<BookRetrievalService>.Instance);

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
