using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Models.DTO;
using FindThatBook.Api.Services.Extraction;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Tests;

public class QueryExtractionServiceTests
{
    private static readonly ExtractedQuery LlmAnswer = new() { Title = "The Hobbit", Author = "Tolkien" };

    private static QueryExtractionService CreateSut(ILlmQueryExtractor llm, string? apiKey) =>
        new(llm,
            new FallbackQueryExtractor(),
            Options.Create(new GeminiOptions
            {
                ApiKey = apiKey,
                Model = "test-model",
                BaseUrl = "https://example.invalid/",
                TimeoutSeconds = 5
            }),
            NullLogger<QueryExtractionService>.Instance);

    [Fact]
    public async Task Uses_the_model_when_it_is_configured_and_answers()
    {
        var sut = CreateSut(new StubLlmExtractor(LlmAnswer), apiKey: "key");

        var result = await sut.ExtractAsync("\"The Hobbit\" by Tolkien", CancellationToken.None);

        Assert.Equal(QueryExtractionSource.Llm, result.Source);
        Assert.Null(result.FallbackReason);
        Assert.Equal("The Hobbit", result.Query.Title);
    }

    /// <summary>
    /// The three ways the model can let us down. All of them must degrade to the
    /// deterministic parser and say so, rather than failing the request or returning
    /// nothing while looking successful.
    /// </summary>
    [Theory]
    [InlineData(null, "not configured")]                 // no API key at all
    [InlineData("key", "unavailable")]                   // call throws
    [InlineData("key", "empty")]                         // call returns nothing usable
    public async Task Falls_back_and_explains_why(string? apiKey, string scenario)
    {
        ILlmQueryExtractor llm = scenario switch
        {
            "unavailable" => new ThrowingLlmExtractor(),
            "empty" => new StubLlmExtractor(new ExtractedQuery()),
            _ => new StubLlmExtractor(LlmAnswer)
        };

        var result = await CreateSut(llm, apiKey).ExtractAsync("\"The Hobbit\" by Tolkien", CancellationToken.None);

        Assert.Equal(QueryExtractionSource.Fallback, result.Source);
        Assert.False(string.IsNullOrWhiteSpace(result.FallbackReason));

        // The deterministic parser still did real work rather than returning nothing.
        Assert.Equal("The Hobbit", result.Query.Title);
        Assert.Equal("Tolkien", result.Query.Author);
    }

    /// <summary>
    /// A client hanging up is not a model failure. Falling back would be wasted work on a
    /// response nobody will read.
    /// </summary>
    [Fact]
    public async Task Does_not_fall_back_when_the_caller_cancels()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var sut = CreateSut(new ThrowingLlmExtractor(cancelling: true), apiKey: "key");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.ExtractAsync("anything", cancelled.Token));
    }

    private sealed class StubLlmExtractor(ExtractedQuery answer) : ILlmQueryExtractor
    {
        public Task<ExtractedQuery> ExtractAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult(answer);
    }

    private sealed class ThrowingLlmExtractor(bool cancelling = false) : ILlmQueryExtractor
    {
        public Task<ExtractedQuery> ExtractAsync(string query, CancellationToken cancellationToken)
        {
            if (cancelling)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new LlmExtractionException("Gemini returned 503.");
        }
    }
}
