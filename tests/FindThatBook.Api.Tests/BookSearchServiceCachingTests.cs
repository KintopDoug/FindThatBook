using FindThatBook.Api.Configuration;
using FindThatBook.Api.Models.DTO;
using FindThatBook.Api.Models.Response;
using FindThatBook.Api.Services;
using FindThatBook.Api.Services.Extraction;
using FindThatBook.Api.Services.Mapping;
using FindThatBook.Api.Services.Ranking;
using FindThatBook.Api.Services.Retrieval;
using FindThatBook.Api.Services.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Tests;

/// <summary>
/// The response cache exists to spare the model, not just Open Library. The retrieval cache
/// is keyed on the interpreted query, so reaching it already costs an extraction call.
/// </summary>
public class BookSearchServiceCachingTests
{
    private static readonly OpenLibraryWork Work = new()
    {
        Key = "/works/OL1W",
        Title = "The Hobbit",
        ContributorNames = ["J.R.R. Tolkien"],
        ContributorKeys = ["OL26320A"],
    };

    private static SearchResponseCache CreateCache() =>
        new(Options.Create(new SearchOptions
        {
            MaxQueryLength = 500,
            ResponseCacheLifetimeMinutes = 30,
            ResponseCacheMaxEntries = 100,
        }));

    private static BookSearchService CreateSut(
        ISearchResponseCache cache,
        CountingExtraction extraction,
        CountingRanking ranking) =>
        new(NullLogger<BookSearchService>.Instance,
            new QueryValidationService(Options.Create(new SearchOptions
            {
                MaxQueryLength = 500,
                ResponseCacheLifetimeMinutes = 30,
                ResponseCacheMaxEntries = 100,
            })),
            extraction,
            new StubRetrieval(),
            ranking,
            new BookCandidateMapper(Options.Create(new OpenLibraryOptions
            {
                BaseUrl = "https://example.invalid/",
                CoverBaseUrl = "https://covers.example.invalid/b/id/",
                UserAgent = "tests",
                TotalTimeoutSeconds = 30,
                MaxResults = 10,
                PrimaryAuthorLookups = 2,
                RequestsPerSecond = 3,
                MaxQueuedRequests = 30,
                CacheLifetimeMinutes = 45,
                CacheMaxEntries = 100,
            })),
            cache);

    [Fact]
    public async Task Repeat_search_costs_no_model_calls()
    {
        var cache = CreateCache();
        var extraction = new CountingExtraction();
        var ranking = new CountingRanking();

        var first = await CreateSut(cache, extraction, ranking)
            .SearchAsync("The Hobbit by Tolkien", CancellationToken.None);

        Assert.Equal(1, extraction.Calls);
        Assert.Equal(1, ranking.Calls);

        // A separate service instance, as a second request would use.
        var second = await CreateSut(cache, extraction, ranking)
            .SearchAsync("The Hobbit by Tolkien", CancellationToken.None);

        Assert.Equal(1, extraction.Calls);   // unchanged: the model was not consulted again
        Assert.Equal(1, ranking.Calls);
        Assert.Equal(first.Results.Count, second.Results.Count);
        Assert.Equal(first.ExtractionSource, second.ExtractionSource);
    }

    /// <summary>
    /// Case folding on the key, so the same request typed differently shares one answer, but
    /// the response still echoes back what this caller actually typed.
    /// </summary>
    [Fact]
    public async Task Differently_capitalised_queries_share_an_answer()
    {
        var cache = CreateCache();
        var extraction = new CountingExtraction();
        var ranking = new CountingRanking();

        await CreateSut(cache, extraction, ranking).SearchAsync("The Hobbit", CancellationToken.None);

        var second = await CreateSut(cache, extraction, ranking)
            .SearchAsync("the hobbit", CancellationToken.None);

        Assert.Equal(1, extraction.Calls);
        Assert.Equal("the hobbit", second.Query);
    }

    /// <summary>
    /// A degraded answer must not be pinned for the cache lifetime: the model may recover in
    /// seconds, and the next caller deserves a real attempt.
    /// </summary>
    [Fact]
    public async Task A_response_degraded_by_an_unreachable_model_is_not_cached()
    {
        var cache = CreateCache();
        var extraction = new CountingExtraction { AiUnavailable = true };
        var ranking = new CountingRanking();

        await CreateSut(cache, extraction, ranking).SearchAsync("The Hobbit", CancellationToken.None);
        await CreateSut(cache, extraction, ranking).SearchAsync("The Hobbit", CancellationToken.None);

        Assert.Equal(2, extraction.Calls);   // tried again rather than replaying a weak answer
    }

    /// <summary>
    /// Falling back because no key is configured is the steady state, not a degradation:
    /// asking again would produce the same answer, so it is worth caching.
    /// </summary>
    [Fact]
    public async Task A_response_from_an_unconfigured_model_is_cached()
    {
        var cache = CreateCache();
        var extraction = new CountingExtraction { NotConfigured = true };
        var ranking = new CountingRanking();

        await CreateSut(cache, extraction, ranking).SearchAsync("The Hobbit", CancellationToken.None);
        await CreateSut(cache, extraction, ranking).SearchAsync("The Hobbit", CancellationToken.None);

        Assert.Equal(1, extraction.Calls);
    }

    private sealed class CountingExtraction : IQueryExtractionService
    {
        public int Calls { get; private set; }
        public bool AiUnavailable { get; init; }
        public bool NotConfigured { get; init; }

        public Task<QueryExtractionResult> ExtractAsync(string query, CancellationToken cancellationToken)
        {
            Calls++;

            var extracted = new ExtractedQuery { Title = "The Hobbit", Author = "Tolkien" };

            if (AiUnavailable)
            {
                return Task.FromResult(
                    QueryExtractionResult.FromFallback(extracted, "unavailable", aiUnavailable: true));
            }

            return Task.FromResult(NotConfigured
                ? QueryExtractionResult.FromFallback(extracted, "not configured")
                : QueryExtractionResult.FromLlm(extracted));
        }
    }

    private sealed class CountingRanking : IBookRankingService
    {
        public int Calls { get; private set; }

        public Task<BookRankingResult> RankAsync(
            string rawQuery,
            ExtractedQuery query,
            IReadOnlyList<OpenLibraryWork> works,
            CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(BookRankingResult.FromLlm(
                works.Select(w => new RankedWork { Work = w, Explanation = "Matched." }).ToArray()));
        }
    }

    private sealed class StubRetrieval : IBookRetrievalService
    {
        public Task<BookRetrievalResult> RetrieveAsync(
            ExtractedQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(new BookRetrievalResult
            {
                Works = [Work],
                Strategy = RetrievalStrategy.TitleAndAuthor,
            });
    }
}
