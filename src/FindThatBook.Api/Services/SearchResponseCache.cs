using FindThatBook.Api.Configuration;
using FindThatBook.Api.Models.Response;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Services
{
    /// <summary>
    /// Remembers whole search responses, keyed on the normalized query.
    /// </summary>
    /// <remarks>
    /// This sits in front of the entire pipeline, which is the point. The retrieval cache is
    /// keyed on the <em>interpreted</em> query, so reaching it already costs an LLM call --
    /// it can save Open Library requests but never the model latency that dominates a repeat
    /// search. The normalized query is the last thing known before any model call and is
    /// produced deterministically, so it is a sound key for the finished answer.
    /// <para>
    /// The retrieval cache still earns its place underneath: differently phrased queries that
    /// interpret the same way miss here and hit there.
    /// </para>
    /// </remarks>
    public sealed class SearchResponseCache : ISearchResponseCache, IDisposable
    {
        private readonly MemoryCache _cache;
        private readonly TimeSpan _lifetime;

        public SearchResponseCache(IOptions<SearchOptions> searchOptions)
        {
            var options = searchOptions.Value;

            _cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = options.ResponseCacheMaxEntries
            });

            _lifetime = TimeSpan.FromMinutes(options.ResponseCacheLifetimeMinutes);
        }

        public bool TryGet(string normalizedQuery, out BookSearchResponse? response) =>
            _cache.TryGetValue(BuildKey(normalizedQuery), out response);

        public void Set(string normalizedQuery, BookSearchResponse response) =>
            _cache.Set(BuildKey(normalizedQuery), response, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _lifetime,
                Size = 1
            });

        /// <summary>
        /// Case is folded so that queries differing only in capitalisation share an answer.
        /// The folding is for lookup only; the normalized query itself keeps its original
        /// casing, because the model and Open Library both treat it as evidence.
        /// </summary>
        internal static string BuildKey(string normalizedQuery) =>
            normalizedQuery.ToLowerInvariant();

        public void Dispose() => _cache.Dispose();
    }
}
