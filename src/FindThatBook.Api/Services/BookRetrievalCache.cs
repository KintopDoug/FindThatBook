using FindThatBook.Api.Configuration;
using FindThatBook.Api.Models.DTO;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Services
{
    /// <summary>
    /// Remembers what an interpreted query retrieved, so repeat searches cost no Open Library
    /// requests at all.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton: <see cref="BookRetrievalService"/> is scoped, so a cache
    /// owned by it would be discarded at the end of every request and never hit. It keeps its
    /// own <see cref="MemoryCache"/> rather than sharing the ambient one, because the size
    /// limit here would otherwise force every other consumer of the shared cache to declare an
    /// entry size.
    /// </remarks>
    public sealed class BookRetrievalCache : IDisposable
    {
        private readonly MemoryCache _cache;
        private readonly TimeSpan _lifetime;

        public BookRetrievalCache(IOptions<OpenLibraryOptions> openLibraryOptions)
        {
            var options = openLibraryOptions.Value;

            _cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = options.CacheMaxEntries
            });

            _lifetime = TimeSpan.FromMinutes(options.CacheLifetimeMinutes);
        }

        public bool TryGet(ExtractedQuery query, out BookRetrievalResult? result) =>
            _cache.TryGetValue(BuildKey(query), out result);

        public void Set(ExtractedQuery query, BookRetrievalResult result) =>
            _cache.Set(BuildKey(query), result, new MemoryCacheEntryOptions
            {
                // Absolute, not sliding: a popular query should still be refreshed on a
                // predictable schedule rather than being kept alive forever by traffic.
                AbsoluteExpirationRelativeToNow = _lifetime,
                Size = 1
            });

        /// <summary>
        /// Builds the identity of an interpreted query.
        /// </summary>
        /// <remarks>
        /// Case is folded and keywords are sorted so that queries which differ only in
        /// capitalisation or word order share an entry. This folding exists purely to compare
        /// two interpretations; the original casing is what actually gets sent to Open Library.
        /// </remarks>
        internal static string BuildKey(ExtractedQuery query) =>
            string.Join(FieldSeparator,
                query.Title?.ToLowerInvariant() ?? string.Empty,
                query.Author?.ToLowerInvariant() ?? string.Empty,
                string.Join(FieldSeparator, query.Keywords
                    .Select(keyword => keyword.ToLowerInvariant())
                    .OrderBy(keyword => keyword, StringComparer.Ordinal)));

        /// <summary>
        /// Unit separator. A printable delimiter could appear inside a title or author and
        /// let two different interpretations collide on a single key.
        /// </summary>
        private const char FieldSeparator = '\u001F';

        public void Dispose() => _cache.Dispose();
    }
}
