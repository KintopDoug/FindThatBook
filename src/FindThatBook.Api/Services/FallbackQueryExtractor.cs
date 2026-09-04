using System.Text.RegularExpressions;
using FindThatBook.Api.Models.DTO;

namespace FindThatBook.Api.Services
{
    /// <summary>
    /// Interprets a query using fixed rules instead of a language model.
    /// </summary>
    /// <remarks>
    /// This is deliberately modest. It recognises the two signals that are unambiguous in
    /// plain text -- a quoted phrase is a title, and text after " by " is an author -- and
    /// otherwise leaves title and author unset rather than guessing. Everything else becomes
    /// keywords, which Open Library can still search on. Guessing harder here would produce
    /// confident wrong answers, which rank worse than an honest broad search.
    /// </remarks>
    public partial class FallbackQueryExtractor : IFallbackQueryExtractor
    {
        /// <summary>
        /// Words that carry no search signal. Kept short on purpose: over-trimming destroys
        /// real titles such as "The Road" or "It".
        /// </summary>
        private static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "of", "and", "or",
            "book", "books", "novel", "story", "find", "about", "something", "that",
            "looking", "for", "me", "i", "want", "need", "read", "please"
        };

        public ExtractedQuery Extract(string query)
        {
            var remainder = query;

            // A quoted phrase is an explicit title: the user has already done the parsing.
            string? title = null;
            var quoted = QuotedPhrase().Match(remainder);
            if (quoted.Success)
            {
                title = quoted.Groups["phrase"].Value.Trim();
                remainder = remainder.Remove(quoted.Index, quoted.Length);
            }

            // " by " is the one author marker that is safe to treat as structural.
            string? author = null;
            var byMatch = ByAuthor().Match(remainder);
            if (byMatch.Success)
            {
                author = byMatch.Groups["author"].Value.Trim();
                remainder = remainder.Remove(byMatch.Index, byMatch.Length);

                // "<something> by <author>" means the something was the title, unless a
                // quoted title already claimed that role.
                var beforeBy = remainder.Trim();
                if (title is null && beforeBy.Length > 0)
                {
                    title = beforeBy;
                    remainder = string.Empty;
                }
            }

            return new ExtractedQuery
            {
                Title = NullIfBlank(title),
                Author = NullIfBlank(author),
                Keywords = ToKeywords(remainder)
            };
        }

        private static string? NullIfBlank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static IReadOnlyList<string> ToKeywords(string remainder) =>
            remainder
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(word => word.Trim('"', '\'', ',', '.', '?', '!', ':', ';'))
                .Where(word => word.Length > 1 && !NoiseWords.Contains(word))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        /// <summary>Matches the first double- or single-quoted phrase.</summary>
        [GeneratedRegex("""["'](?<phrase>[^"']+)["']""")]
        private static partial Regex QuotedPhrase();

        /// <summary>Matches " by &lt;author&gt;" running to the end of the query.</summary>
        [GeneratedRegex(@"\bby\s+(?<author>.+)$", RegexOptions.IgnoreCase)]
        private static partial Regex ByAuthor();
    }
}
