using FindThatBook.Api.Configuration;
using FindThatBook.Api.Models.DTO;
using FindThatBook.Api.Services;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Tests;

public class BookRetrievalCacheTests
{
    private static BookRetrievalCache CreateSut(int lifetimeMinutes = 45, int maxEntries = 100) =>
        new(Options.Create(new OpenLibraryOptions
        {
            BaseUrl = "https://example.invalid/",
            CoverBaseUrl = "https://covers.example.invalid/b/id/",
            UserAgent = "tests",
            TotalTimeoutSeconds = 5,
            MaxResults = 10,
            PrimaryAuthorLookups = 2,
            CacheLifetimeMinutes = lifetimeMinutes,
            CacheMaxEntries = maxEntries
        }));

    private static BookRetrievalResult Result() => new()
    {
        Works = [new OpenLibraryWork { Key = "/works/OL1W", Title = "The Hobbit" }],
        Strategy = RetrievalStrategy.TitleAndAuthor
    };

    /// <summary>
    /// Interpretations that mean the same thing must share an entry, or the cache stops
    /// absorbing the repeat traffic it exists to absorb.
    /// </summary>
    [Fact]
    public void Treats_equivalent_interpretations_as_one_entry()
    {
        using var sut = CreateSut();

        sut.Set(
            new ExtractedQuery { Title = "The Hobbit", Author = "Tolkien", Keywords = ["fantasy", "quest"] },
            Result());

        // Different casing, and keywords in a different order.
        var hit = sut.TryGet(
            new ExtractedQuery { Title = "the hobbit", Author = "TOLKIEN", Keywords = ["quest", "fantasy"] },
            out var cached);

        Assert.True(hit);
        Assert.NotNull(cached);
    }

    /// <summary>
    /// Fields must stay distinguishable. Without a separator that cannot occur in the text,
    /// a title ending where an author begins would collide.
    /// </summary>
    [Theory]
    [InlineData("Dickens", null, null, "Dickens")]          // title vs author
    [InlineData("A B", null, "A", "B")]                     // one title vs two keywords
    public void Keeps_different_interpretations_apart(
        string? titleA,
        string? authorA,
        string? titleB,
        string keywordB)
    {
        using var sut = CreateSut();

        sut.Set(new ExtractedQuery { Title = titleA, Author = authorA }, Result());

        var hit = sut.TryGet(
            new ExtractedQuery { Title = titleB, Keywords = [keywordB] },
            out _);

        Assert.False(hit);
    }

    [Fact]
    public void Reports_a_miss_for_an_interpretation_it_has_not_seen()
    {
        using var sut = CreateSut();

        sut.Set(new ExtractedQuery { Title = "The Hobbit" }, Result());

        Assert.False(sut.TryGet(new ExtractedQuery { Title = "Moby Dick" }, out _));
    }
}
