using FindThatBook.Api.Models.DTO;

namespace FindThatBook.Api.Services.Ranking
{
    /// <summary>
    /// Orders candidates by measurable title and author evidence.
    /// </summary>
    /// <remarks>
    /// Scores are an internal ordering device and are never shown. The brief asks for ordering
    /// and an explanation, not a number, and a number would imply a precision this does not
    /// have. What the user sees is the position and the sentence, and the sentence names only
    /// evidence that actually contributed to the score.
    /// </remarks>
    public class FallbackBookRanker : IFallbackBookRanker
    {
        // Title is the stronger signal when both are present: authors write many books.
        private const double TitleWeight = 0.6;
        private const double AuthorWeight = 0.4;

        /// <summary>
        /// Reward for an author the canonical work record confirms, which is how the brief's
        /// data-quality problem gets resolved in the ordering rather than only in the text:
        /// a confirmed author outranks a name that merely appears in the contributor list.
        /// </summary>
        private const double ConfirmedAuthorBonus = 0.15;

        /// <summary>
        /// Penalty when the catalogue was consulted and the matched name is not a primary
        /// author. Applied only when we actually checked; an unexamined work is not demoted
        /// on a suspicion.
        /// </summary>
        private const double ContributorOnlyPenalty = 0.2;

        public IReadOnlyList<RankedWork> Rank(ExtractedQuery query, IReadOnlyList<OpenLibraryWork> works)
        {
            if (works.Count == 0)
            {
                return [];
            }

            return works
                .Select(work => Score(query, work))
                .OrderByDescending(scored => scored.Score)
                // Ties broken by how widely published the work is, then by age. Both favour the
                // canonical edition of a classic over a niche adaptation of it.
                .ThenByDescending(scored => scored.Work.EditionCount ?? 0)
                .ThenBy(scored => scored.Work.FirstPublishYear ?? int.MaxValue)
                .Select(scored => new RankedWork
                {
                    Work = scored.Work,
                    Explanation = Explain(scored)
                })
                .ToArray();
        }

        private static ScoredWork Score(ExtractedQuery query, OpenLibraryWork work)
        {
            var title = ScoreTitle(query.Title, work);
            var author = ScoreAuthor(query.Author, work);

            var hasTitle = !string.IsNullOrWhiteSpace(query.Title);
            var hasAuthor = !string.IsNullOrWhiteSpace(query.Author);

            var keywords = hasTitle || hasAuthor
                ? new KeywordEvidence(0, false, null)
                : ScoreKeywords(query, work);

            // Only weigh what the query actually supplied, so a title-only query is not
            // penalised for having no author to match.
            var score = (hasTitle, hasAuthor) switch
            {
                (true, true) => (title.Score * TitleWeight) + (author.Score * AuthorWeight),
                (true, false) => title.Score,
                (false, true) => author.Score,
                _ => keywords.Score
            };

            // The brief's central ranking rule: a confirmed primary author is stronger
            // evidence than the same name appearing only among contributors, where it may
            // belong to an illustrator, editor, or adaptor.
            score += author switch
            {
                { Score: > 0, Standing: AuthorStanding.Confirmed } => ConfirmedAuthorBonus,
                { Score: > 0, Standing: AuthorStanding.ContributorOnly } => -ContributorOnlyPenalty,
                _ => 0
            };

            return new ScoredWork(work, score, title, author, keywords);
        }

        private static TitleEvidence ScoreTitle(string? queryTitle, OpenLibraryWork work)
        {
            if (string.IsNullOrWhiteSpace(queryTitle))
            {
                return new TitleEvidence(0, TitleMatch.None);
            }

            var wanted = MatchNormalizer.Normalize(queryTitle);
            var actual = MatchNormalizer.Normalize(work.Title);
            var actualWithoutSubtitle = MatchNormalizer.StripSubtitle(work.Title);

            if (wanted == actual)
            {
                return new TitleEvidence(1.0, TitleMatch.Exact);
            }

            if (wanted == actualWithoutSubtitle)
            {
                return new TitleEvidence(0.95, TitleMatch.ExactIgnoringSubtitle);
            }

            if (actual.StartsWith(wanted, StringComparison.Ordinal))
            {
                return new TitleEvidence(0.8, TitleMatch.Prefix);
            }

            if (actual.Contains(wanted, StringComparison.Ordinal))
            {
                return new TitleEvidence(0.65, TitleMatch.Contains);
            }

            var overlap = MatchNormalizer.TokenOverlap(
                MatchNormalizer.Tokens(queryTitle, dropNoise: true),
                MatchNormalizer.Tokens(work.Title, dropNoise: true));

            return overlap > 0
                ? new TitleEvidence(overlap * 0.6, TitleMatch.Partial)
                : new TitleEvidence(0, TitleMatch.None);
        }

        private static AuthorEvidence ScoreAuthor(string? queryAuthor, OpenLibraryWork work)
        {
            // Match against every listed name, not just the confirmed ones, so a contributor
            // match is detected rather than missed. Its standing is judged separately.
            var listed = work.ContributorNames.Count > 0 ? work.ContributorNames : work.BestKnownAuthors;

            if (string.IsNullOrWhiteSpace(queryAuthor) || listed.Count == 0)
            {
                return new AuthorEvidence(0, AuthorMatch.None, null, AuthorStanding.Unverified);
            }

            var wanted = MatchNormalizer.Normalize(queryAuthor);
            var wantedSurname = MatchNormalizer.Surname(queryAuthor);

            AuthorEvidence best = new(0, AuthorMatch.None, null, AuthorStanding.Unverified);

            foreach (var candidate in listed)
            {
                var actual = MatchNormalizer.Normalize(candidate);

                var (score, match) = actual == wanted
                    ? (1.0, AuthorMatch.Exact)
                    : MatchNormalizer.Surname(candidate) == wantedSurname && wantedSurname.Length > 0
                        ? (0.85, AuthorMatch.Surname)
                        : actual.Contains(wanted, StringComparison.Ordinal)
                            ? (0.7, AuthorMatch.Contains)
                            : (MatchNormalizer.TokenOverlap(
                                MatchNormalizer.Tokens(queryAuthor),
                                MatchNormalizer.Tokens(candidate)) * 0.6, AuthorMatch.Partial);

                if (score > best.Score)
                {
                    best = new AuthorEvidence(score, match, candidate, Standing(work, candidate));
                }
            }

            return best.Score > 0
                ? best
                : new AuthorEvidence(0, AuthorMatch.None, null, AuthorStanding.Unverified);
        }

        /// <summary>
        /// What the catalogue actually tells us about a matched name.
        /// </summary>
        private static AuthorStanding Standing(OpenLibraryWork work, string name)
        {
            if (!work.PrimaryAuthorsChecked)
            {
                return AuthorStanding.Unverified;
            }

            var normalized = MatchNormalizer.Normalize(name);

            return work.PrimaryAuthorNames.Any(primary =>
                MatchNormalizer.Normalize(primary) == normalized)
                ? AuthorStanding.Confirmed
                : AuthorStanding.ContributorOnly;
        }

        /// <summary>
        /// With no title and no author, all we have is keywords against the title. Kept
        /// modest so a keyword hit never outranks a real title or author match.
        /// </summary>
        private static KeywordEvidence ScoreKeywords(ExtractedQuery query, OpenLibraryWork work)
        {
            // Keywords are searched against titles AND author names. When interpretation
            // could not tell a title from an author, a bare surname like "dickens" arrives as
            // a keyword, and comparing it only against titles would score every result zero.
            var wanted = query.Keywords
                .SelectMany(keyword => MatchNormalizer.Tokens(keyword))
                .ToArray();

            if (wanted.Length == 0)
            {
                return new KeywordEvidence(0, false, null);
            }

            var titleOverlap = MatchNormalizer.TokenOverlap(wanted, MatchNormalizer.Tokens(work.Title));

            string? matchedAuthor = null;

            foreach (var author in work.BestKnownAuthors)
            {
                if (MatchNormalizer.TokenOverlap(wanted, MatchNormalizer.Tokens(author)) > 0)
                {
                    matchedAuthor = author;
                    break;
                }
            }

            var authorOverlap = matchedAuthor is null
                ? 0
                : MatchNormalizer.TokenOverlap(wanted, MatchNormalizer.Tokens(matchedAuthor));

            return new KeywordEvidence(
                Math.Max(titleOverlap, authorOverlap) * 0.5,
                titleOverlap > 0,
                matchedAuthor);
        }

        private static string Explain(ScoredWork scored)
        {
            var parts = new List<string>();

            var titlePart = scored.Title.Match switch
            {
                TitleMatch.Exact => "Title matches exactly",
                TitleMatch.ExactIgnoringSubtitle => "Title matches apart from the subtitle",
                TitleMatch.Prefix => "Title begins with the words searched for",
                TitleMatch.Contains => "Title contains the words searched for",
                TitleMatch.Partial => "Some title words match",
                _ => null
            };

            if (titlePart is not null)
            {
                parts.Add(titlePart);
            }

            var authorPart = scored.Author.Match switch
            {
                AuthorMatch.Exact => $"author matches {scored.Author.Name}",
                AuthorMatch.Surname => $"author surname matches {scored.Author.Name}",
                AuthorMatch.Contains => $"author name contains the search term ({scored.Author.Name})",
                AuthorMatch.Partial => $"some author words match ({scored.Author.Name})",
                _ => null
            };

            if (authorPart is not null)
            {
                // State the evidence we actually have. Claiming a name "may be a contributor"
                // when the work record was never opened would be an accusation, not a finding.
                parts.Add(scored.Author.Standing switch
                {
                    AuthorStanding.Confirmed => $"{authorPart}, confirmed as a primary author",
                    AuthorStanding.ContributorOnly =>
                        $"{authorPart}, but the work record lists them as a contributor rather than the author",
                    _ => $"{authorPart}, not verified against the work record"
                });
            }

            // Keyword evidence only exists when no title or author was identified, so it never
            // competes with the sentences above.
            if (scored.Keywords.MatchedAuthor is { } keywordAuthor)
            {
                parts.Add($"search terms match the author {keywordAuthor}");
            }

            if (scored.Keywords.MatchedTitle)
            {
                parts.Add("search terms appear in the title");
            }

            if (parts.Count == 0)
            {
                return "Returned by Open Library for this search, with no direct title or author match.";
            }

            var sentence = string.Join("; ", parts);

            return char.ToUpperInvariant(sentence[0]) + sentence[1..] + ".";
        }

        private enum TitleMatch { None, Partial, Contains, Prefix, ExactIgnoringSubtitle, Exact }

        private enum AuthorMatch { None, Partial, Contains, Surname, Exact }

        private sealed record TitleEvidence(double Score, TitleMatch Match);

        /// <summary>What the catalogue says about a matched name, as opposed to how well it matched.</summary>
        private enum AuthorStanding { Unverified, ContributorOnly, Confirmed }

        private sealed record AuthorEvidence(
            double Score,
            AuthorMatch Match,
            string? Name,
            AuthorStanding Standing);

        private sealed record KeywordEvidence(double Score, bool MatchedTitle, string? MatchedAuthor);

        private sealed record ScoredWork(
            OpenLibraryWork Work,
            double Score,
            TitleEvidence Title,
            AuthorEvidence Author,
            KeywordEvidence Keywords);
    }
}
