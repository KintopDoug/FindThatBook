using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Services;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Tests;

public class QueryValidationServiceTests
{
    private static QueryValidationService CreateSut(int maxQueryLength = 500) =>
        new(Options.Create(new SearchOptions { MaxQueryLength = maxQueryLength }));

    /// <summary>
    /// Anything a user cannot see is not content. Invisible-only queries are the
    /// interesting cases here: they are non-empty strings, so a naive length check
    /// would wave them through.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u200B")]      // zero-width space
    [InlineData("\u00AD")]      // soft hyphen
    [InlineData("\uFEFF")]      // byte order mark
    [InlineData("  \u200B  ")]  // invisible padded with real whitespace
    public void Rejects_queries_with_no_visible_content(string? query)
    {
        var exception = Assert.Throws<InvalidQueryException>(
            () => CreateSut().NormalizeAndValidate(query));

        Assert.Equal("Query must not be empty.", exception.Message);
    }

    [Theory]
    // Trims, and collapses whitespace runs including tabs and newlines from a paste.
    [InlineData("  tale two cities  ", "tale two cities")]
    [InlineData("tale   two    cities", "tale two cities")]
    [InlineData("tale\ttwo\ncities", "tale two cities")]
    [InlineData("tale\u00A0two cities", "tale two cities")]
    // Invisible characters are removed, leaving the text the user actually sees.
    [InlineData("hob\u00ADbit", "hobbit")]
    [InlineData("tale\u200Btwo cities", "taletwo cities")]
    // Composes to NFC, so both spellings of the same accented text agree.
    [InlineData("Garci\u0301a", "Garc\u00EDa")]
    // Case, punctuation and diacritics are evidence for the LLM and Open Library
    // stages, so normalization must leave them alone.
    [InlineData("Tolkien's \"The Hobbit\"", "Tolkien's \"The Hobbit\"")]
    public void Normalizes_to_visible_composed_text(string query, string expected)
    {
        Assert.Equal(expected, CreateSut().NormalizeAndValidate(query));
    }

    /// <summary>
    /// The limit has to apply to normalized content, not to the raw request. Padding and
    /// invisible characters must not consume the caller's budget, and the boundary itself
    /// is inclusive. Uses a small configured limit so the value is demonstrably read from
    /// options rather than hard-coded.
    /// </summary>
    [Fact]
    public void Measures_length_against_normalized_text()
    {
        var sut = CreateSut(maxQueryLength: 10);

        Assert.Equal("abcdefghij", sut.NormalizeAndValidate("  abcdefghij\u200B  "));

        var exception = Assert.Throws<InvalidQueryException>(
            () => sut.NormalizeAndValidate("abcdefghijk"));

        Assert.Equal("Query must be 10 characters or fewer.", exception.Message);
    }

    /// <summary>
    /// Text that cannot be Unicode-normalized is caller error, not a server fault. Without
    /// this the underlying ArgumentException would surface as a 500.
    /// </summary>
    [Fact]
    public void Rejects_malformed_text()
    {
        var exception = Assert.Throws<InvalidQueryException>(
            () => CreateSut().NormalizeAndValidate("lone surrogate \uD800 here"));

        Assert.Equal("Query contains invalid text.", exception.Message);
    }
}
