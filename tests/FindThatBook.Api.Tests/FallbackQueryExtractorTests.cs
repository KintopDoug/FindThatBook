using FindThatBook.Api.Services;

namespace FindThatBook.Api.Tests;

public class FallbackQueryExtractorTests
{
    private static FallbackQueryExtractor CreateSut() => new();

    /// <summary>
    /// The two structural signals the parser is allowed to trust: a quoted phrase is a
    /// title, and text after "by" is an author.
    /// </summary>
    [Theory]
    [InlineData("tale of two cities by dickens", "tale of two cities", "dickens")]
    [InlineData("\"The Hobbit\" by Tolkien", "The Hobbit", "Tolkien")]
    [InlineData("\"The Road\"", "The Road", null)]
    [InlineData("something by Gabriel Garcia Marquez", "something", "Gabriel Garcia Marquez")]
    public void Reads_quoted_titles_and_by_authors(string query, string? title, string? author)
    {
        var result = CreateSut().Extract(query);

        Assert.Equal(title, result.Title);
        Assert.Equal(author, result.Author);
    }

    /// <summary>
    /// With no structural marker the parser must not guess. A bare word could be a title or
    /// an author, so it becomes a keyword and lets the search stage decide.
    /// </summary>
    [Theory]
    [InlineData("dickens", "dickens")]
    [InlineData("tale two cities", "tale,two,cities")]
    [InlineData("find me a book about whales", "whales")]
    public void Leaves_title_and_author_unset_without_a_marker(string query, string expectedKeywords)
    {
        var result = CreateSut().Extract(query);

        Assert.Null(result.Title);
        Assert.Null(result.Author);
        Assert.Equal(expectedKeywords.Split(','), result.Keywords);
    }

    [Fact]
    public void Reports_empty_when_nothing_usable_remains()
    {
        // Every word is noise, so there is no signal to search on.
        Assert.True(CreateSut().Extract("find me a book").IsEmpty);
    }
}
