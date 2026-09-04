using System.Text;
using System.Text.RegularExpressions;
using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Services
{
    public partial class QueryValidationService(IOptions<SearchOptions> searchOptions) : IQueryValidationService
    {
        private readonly SearchOptions _searchOptions = searchOptions.Value;

        public string NormalizeAndValidate(string? query)
        {
            // Normalize before validating, so the length limit applies to real content
            // rather than to padding, and so every later stage sees the same text.
            var normalized = Normalize(query);

            //query may not be null, empty, or whitespace
            if (normalized.Length == 0)
                throw InvalidQueryException.Empty();

            //query may not exceed length specified in SearchOptions.MaxQueryLength
            if (normalized.Length > _searchOptions.MaxQueryLength)
                throw InvalidQueryException.TooLong(_searchOptions.MaxQueryLength);

            return normalized;
        }

        /// <summary>
        /// Reduces a raw query to the text the user can actually see: composed Unicode,
        /// no invisible characters, trimmed, with internal whitespace runs collapsed.
        /// </summary>
        /// <remarks>
        /// Whitespace and invisible characters only. Case, punctuation, and diacritics are
        /// deliberately preserved: they are evidence for the LLM extraction step and for
        /// Open Library relevance, so flattening them here would discard signal the later
        /// stages need. Comparison-time folding belongs in the matcher, applied to query
        /// and result data alike.
        /// </remarks>
        private static string Normalize(string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            // Compose to NFC so that visually identical text is the same string. Without
            // this, "Garcia" typed with a precomposed i and with a combining accent are
            // different strings of different lengths, and every later comparison and cache
            // key disagrees about them.
            string composed;
            try
            {
                composed = query.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                // Unpaired surrogates and similar malformed text. Report it as caller error
                // rather than letting it surface as a 500.
                throw InvalidQueryException.Malformed();
            }

            // Strip invisible formatting and control characters. A zero-width space or soft
            // hyphen renders as nothing, so removing it yields the text the user sees, and
            // stops a query of nothing but invisibles from counting as non-empty content.
            var visible = InvisibleCharacters().Replace(composed, string.Empty);

            return WhitespaceRuns().Replace(visible.Trim(), " ");
        }

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRuns();

        /// <summary>
        /// Unicode format characters (zero-width space, soft hyphen, BOM, bidi marks) plus
        /// control characters, excluding whitespace controls like tab and newline so those
        /// still collapse into spaces rather than gluing words together.
        /// </summary>
        [GeneratedRegex(@"[\p{Cf}\p{Cc}-[\s]]")]
        private static partial Regex InvisibleCharacters();
    }
}
