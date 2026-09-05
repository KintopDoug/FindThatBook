using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FindThatBook.Api.Services.Ranking
{
    /// <summary>
    /// Folds text for comparison only.
    /// </summary>
    /// <remarks>
    /// This is the counterpart to the ingress normalization in
    /// <see cref="FindThatBook.Api.Services.Validation.QueryValidationService"/>, and the two exist for opposite reasons. Ingress
    /// preserves case, punctuation, and accents because they are evidence for the model and
    /// for Open Library relevance. Here we are asking whether two strings mean the same book,
    /// so all of that has to go: "GARCIA MARQUEZ", "Garcia Marquez", and "Garcia Marquez"
    /// are one author, and "The Hobbit: or There and Back Again" is one title with "The
    /// Hobbit". Applied symmetrically to the query and to Open Library data.
    /// </remarks>
    public static partial class MatchNormalizer
    {
        /// <summary>
        /// Words that carry no distinguishing weight in a title. Deliberately tiny: dropping
        /// more would erase real titles such as "It" or "The Road".
        /// </summary>
        private static readonly HashSet<string> TitleNoise =
            new(StringComparer.Ordinal) { "a", "an", "the" };

        /// <summary>Lower-cases, removes accents and punctuation, and collapses whitespace.</summary>
        public static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            // Decompose so accents become separate marks that can be dropped, leaving the
            // base letters: "Marquez" and "Marquez" then compare equal.
            var decomposed = value.Normalize(NormalizationForm.FormD);

            var builder = new StringBuilder(decomposed.Length);

            foreach (var character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
                else if (char.IsWhiteSpace(character) || character is '-' or '\'' or '.')
                {
                    // Punctuation that joins words becomes a space rather than vanishing, so
                    // "J.R.R." and "J R R" agree.
                    builder.Append(' ');
                }
            }

            return WhitespaceRuns().Replace(builder.ToString(), " ").Trim();
        }

        /// <summary>
        /// Drops a subtitle. Open Library and users disagree constantly about whether the
        /// subtitle is part of the title.
        /// </summary>
        public static string StripSubtitle(string? title)
        {
            var normalized = Normalize(title);

            if (string.IsNullOrEmpty(normalized))
            {
                return string.Empty;
            }

            // Normalize has already removed the colon, so split on the original text.
            var separator = title!.IndexOf(':');

            return separator > 0 ? Normalize(title[..separator]) : normalized;
        }

        /// <summary>Significant words of a normalized string.</summary>
        public static IReadOnlyList<string> Tokens(string? value, bool dropNoise = false)
        {
            var normalized = Normalize(value);

            if (normalized.Length == 0)
            {
                return [];
            }

            var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return dropNoise
                ? tokens.Where(token => !TitleNoise.Contains(token)).ToArray()
                : tokens;
        }

        /// <summary>
        /// Last name of an author. Users type "tolkien" far more often than "J.R.R. Tolkien",
        /// so a surname match is the common case rather than an edge case.
        /// </summary>
        public static string Surname(string? author)
        {
            var tokens = Tokens(author);

            return tokens.Count == 0 ? string.Empty : tokens[^1];
        }

        /// <summary>
        /// Fraction of <paramref name="wanted"/> tokens present in <paramref name="available"/>.
        /// </summary>
        public static double TokenOverlap(IReadOnlyList<string> wanted, IReadOnlyList<string> available)
        {
            if (wanted.Count == 0 || available.Count == 0)
            {
                return 0;
            }

            var pool = new HashSet<string>(available, StringComparer.Ordinal);

            return (double)wanted.Count(pool.Contains) / wanted.Count;
        }

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRuns();
    }
}
