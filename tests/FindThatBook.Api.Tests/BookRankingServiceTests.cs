using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Models.DTO;
using FindThatBook.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Tests;

public class BookRankingServiceTests
{
    private static readonly OpenLibraryWork Hobbit = new()
    {
        Key = "/works/hobbit",
        Title = "The Hobbit",
        ContributorNames = ["J.R.R. Tolkien"],
        ContributorKeys = ["OL26320A"],
        PrimaryAuthorNames = ["J.R.R. Tolkien"]
    };

    private static readonly OpenLibraryWork Other = new()
    {
        Key = "/works/other",
        Title = "Something Else",
        ContributorNames = ["Someone"],
        ContributorKeys = ["OL1A"]
    };

    private static BookRankingService CreateSut(ILlmBookRanker llm, string? apiKey) =>
        new(llm,
            new FallbackBookRanker(),
            Options.Create(new GeminiOptions
            {
                ApiKey = apiKey,
                Model = "test-model",
                BaseUrl = "https://example.invalid/",
                TimeoutSeconds = 5
            }),
            NullLogger<BookRankingService>.Instance);

    [Fact]
    public async Task Uses_the_model_when_it_is_configured_and_answers()
    {
        var llm = new StubRanker([
            new RankedWork { Work = Hobbit, Explanation = "Strong title and primary-author match." }
        ]);

        var result = await CreateSut(llm, apiKey: "key")
            .RankAsync("hobbit tolkien", new ExtractedQuery { Title = "The Hobbit" }, [Hobbit], CancellationToken.None);

        Assert.Equal(RankingSource.Llm, result.Source);
        Assert.Null(result.FallbackReason);
        Assert.Equal("Strong title and primary-author match.", result.Ranked[0].Explanation);
    }

    /// <summary>
    /// Both ways the model can be absent must degrade to deterministic ranking and say so,
    /// rather than failing the search or returning an unordered list that looks ranked.
    /// </summary>
    [Theory]
    [InlineData(null)]      // no API key
    [InlineData("key")]     // key present, call fails
    public async Task Falls_back_and_explains_why(string? apiKey)
    {
        ILlmBookRanker llm = apiKey is null
            ? new StubRanker([])
            : new ThrowingRanker();

        var result = await CreateSut(llm, apiKey).RankAsync(
            "hobbit tolkien",
            new ExtractedQuery { Title = "The Hobbit", Author = "Tolkien" },
            [Other, Hobbit],
            CancellationToken.None);

        Assert.Equal(RankingSource.Fallback, result.Source);
        Assert.False(string.IsNullOrWhiteSpace(result.FallbackReason));

        // The deterministic ranker still did real work: the matching book came first.
        Assert.Equal("/works/hobbit", result.Ranked[0].Work.Key);
        Assert.Equal(2, result.Ranked.Count);
    }

    [Fact]
    public async Task Does_not_fall_back_when_the_caller_cancels()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var sut = CreateSut(new ThrowingRanker(cancelling: true), apiKey: "key");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.RankAsync("anything", new ExtractedQuery { Title = "x" }, [Hobbit], cancelled.Token));
    }

    private sealed class StubRanker(IReadOnlyList<RankedWork> answer) : ILlmBookRanker
    {
        public Task<IReadOnlyList<RankedWork>> RankAsync(
            string rawQuery,
            ExtractedQuery query,
            IReadOnlyList<OpenLibraryWork> works,
            CancellationToken cancellationToken) => Task.FromResult(answer);
    }

    private sealed class ThrowingRanker(bool cancelling = false) : ILlmBookRanker
    {
        public Task<IReadOnlyList<RankedWork>> RankAsync(
            string rawQuery,
            ExtractedQuery query,
            IReadOnlyList<OpenLibraryWork> works,
            CancellationToken cancellationToken)
        {
            if (cancelling)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new LlmRankingException("Gemini returned 503.");
        }
    }
}
