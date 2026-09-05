using FindThatBook.Api.Models.DTO;
using FindThatBook.Api.Services;

namespace FindThatBook.Api.Tests;

public class FallbackBookRankerTests
{
    private static FallbackBookRanker CreateSut() => new();

    private static OpenLibraryWork Work(
        string key,
        string title,
        string[]? authors = null,
        string[]? primaryAuthors = null,
        int? year = null,
        int? editions = null,
        string? subtitle = null,
        bool checkedPrimaryAuthors = false) =>
        new()
        {
            Key = key,
            Title = title,
            Subtitle = subtitle,
            ContributorNames = authors ?? [],
            ContributorKeys = (authors ?? []).Select((_, i) => $"OL{i}A").ToArray(),
            PrimaryAuthorNames = primaryAuthors ?? [],
            PrimaryAuthorsChecked = checkedPrimaryAuthors || primaryAuthors is { Length: > 0 },
            FirstPublishYear = year,
            EditionCount = editions
        };

    private static string[] Order(IReadOnlyList<RankedWork> ranked) =>
        ranked.Select(r => r.Work.Key).ToArray();

    /// <summary>
    /// The brief's data-quality case, decided in the ordering rather than only described in
    /// the text: an adaptation listing the author among contributors must not outrank the
    /// work whose primary author the catalogue confirms.
    /// </summary>
    [Fact]
    public void Prefers_a_confirmed_primary_author_over_an_unconfirmed_contributor()
    {
        var adaptation = Work("/works/adaptation", "A Tale of Two Cities",
            authors: ["Ralph Mowat", "Charles Dickens"]);

        var canonical = Work("/works/canonical", "A Tale of Two Cities",
            authors: ["Charles Dickens"], primaryAuthors: ["Charles Dickens"]);

        var ranked = CreateSut().Rank(
            new ExtractedQuery { Title = "a tale of two cities", Author = "dickens" },
            [adaptation, canonical]);

        Assert.Equal(["/works/canonical", "/works/adaptation"], Order(ranked));
        Assert.Contains("confirmed as a primary author", ranked[0].Explanation);
    }

    /// <summary>
    /// Three different states, three different sentences. Saying a name "may be a contributor"
    /// when the work record was never opened is an accusation rather than a finding, and only
    /// the middle case is real contributor evidence.
    /// </summary>
    [Fact]
    public void Distinguishes_confirmed_contributor_only_and_unchecked_authors()
    {
        var query = new ExtractedQuery { Title = "The Hobbit", Author = "Tolkien" };

        var confirmed = CreateSut().Rank(query,
            [Work("/works/1", "The Hobbit",
                authors: ["J.R.R. Tolkien"], primaryAuthors: ["J.R.R. Tolkien"])]);

        // Checked, and the matched name is not among the primary authors.
        var contributorOnly = CreateSut().Rank(query,
            [Work("/works/2", "The Hobbit",
                authors: ["J.R.R. Tolkien"], primaryAuthors: ["Alan Lee"], checkedPrimaryAuthors: true)]);

        // Never looked up, so nothing is known either way.
        var unchecked_ = CreateSut().Rank(query,
            [Work("/works/3", "The Hobbit", authors: ["J.R.R. Tolkien"])]);

        Assert.Contains("confirmed as a primary author", confirmed[0].Explanation);
        Assert.Contains("contributor rather than the author", contributorOnly[0].Explanation);
        Assert.Contains("not verified against the work record", unchecked_[0].Explanation);
    }

    /// <summary>
    /// A name the catalogue actively says is not the author is weaker evidence than one we
    /// simply have not checked, so it must rank below it.
    /// </summary>
    [Fact]
    public void Ranks_a_contributor_only_match_below_an_unchecked_one()
    {
        var contributorOnly = Work("/works/contributor", "The Hobbit",
            authors: ["J.R.R. Tolkien"], primaryAuthors: ["Alan Lee"], checkedPrimaryAuthors: true);

        var unchecked_ = Work("/works/unchecked", "The Hobbit", authors: ["J.R.R. Tolkien"]);

        var ranked = CreateSut().Rank(
            new ExtractedQuery { Title = "The Hobbit", Author = "Tolkien" },
            [contributorOnly, unchecked_]);

        Assert.Equal(["/works/unchecked", "/works/contributor"], Order(ranked));
    }

    /// <summary>
    /// When interpretation cannot tell a title from an author, a bare surname arrives as a
    /// keyword. Scoring it against titles alone would give every candidate zero.
    /// </summary>
    [Fact]
    public void Matches_keywords_against_author_names_as_well_as_titles()
    {
        var byDickens = Work("/works/dickens", "Great Expectations", authors: ["Charles Dickens"]);
        var unrelated = Work("/works/other", "Some Other Book", authors: ["Someone Else"]);

        var ranked = CreateSut().Rank(
            new ExtractedQuery { Keywords = ["dickens"] },
            [unrelated, byDickens]);

        Assert.Equal(["/works/dickens", "/works/other"], Order(ranked));
    }

    /// <summary>
    /// Comparison folding: users do not type accents, capitals, or punctuation the way the
    /// catalogue stores them, and a subtitle should not cost a title its match.
    /// </summary>
    [Theory]
    [InlineData("the hobbit", "The Hobbit", null)]
    [InlineData("THE HOBBIT", "The Hobbit", null)]
    [InlineData("the hobbit", "The Hobbit", "or There and Back Again")]
    [InlineData("cien anos de soledad", "Cien años de soledad", null)]
    [InlineData("j r r tolkien s book", "J.R.R. Tolkien's Book", null)]
    public void Matches_titles_across_case_accents_punctuation_and_subtitles(
        string queryTitle,
        string workTitle,
        string? subtitle)
    {
        var ranked = CreateSut().Rank(
            new ExtractedQuery { Title = queryTitle },
            [Work("/works/1", workTitle, subtitle: subtitle)]);

        Assert.StartsWith("Title matches", ranked[0].Explanation);
    }

    /// <summary>Users type surnames far more often than full names.</summary>
    [Fact]
    public void Matches_an_author_on_surname_alone()
    {
        var ranked = CreateSut().Rank(
            new ExtractedQuery { Title = "The Hobbit", Author = "tolkien" },
            [Work("/works/1", "The Hobbit",
                authors: ["J.R.R. Tolkien"], primaryAuthors: ["J.R.R. Tolkien"])]);

        Assert.Contains("author surname matches J.R.R. Tolkien", ranked[0].Explanation);
    }

    /// <summary>
    /// With equal evidence, the widely published original should beat the obscure edition.
    /// </summary>
    [Fact]
    public void Breaks_ties_toward_the_more_widely_published_work()
    {
        var obscure = Work("/works/obscure", "The Hobbit", editions: 2, year: 1990);
        var canonical = Work("/works/canonical", "The Hobbit", editions: 300, year: 1937);

        var ranked = CreateSut().Rank(new ExtractedQuery { Title = "The Hobbit" }, [obscure, canonical]);

        Assert.Equal(["/works/canonical", "/works/obscure"], Order(ranked));
    }

    /// <summary>
    /// Every candidate must survive ranking. Dropping results here would silently lose books
    /// the catalogue did return.
    /// </summary>
    [Fact]
    public void Keeps_every_candidate_even_with_no_direct_match()
    {
        var works = new[]
        {
            Work("/works/1", "Something Else"),
            Work("/works/2", "Another Thing")
        };

        var ranked = CreateSut().Rank(new ExtractedQuery { Title = "The Hobbit" }, works);

        Assert.Equal(2, ranked.Count);
        Assert.All(ranked, r => Assert.False(string.IsNullOrWhiteSpace(r.Explanation)));
    }
}
