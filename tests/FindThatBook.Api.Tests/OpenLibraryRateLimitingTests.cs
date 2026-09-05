using System.Diagnostics;
using System.Net;
using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace FindThatBook.Api.Tests;

public class OpenLibraryRateLimitingTests
{
    private static OpenLibraryOptions Options(int requestsPerSecond = 3, int maxQueued = 30) => new()
    {
        BaseUrl = "https://openlibrary.test/",
        CoverBaseUrl = "https://covers.openlibrary.test/b/id/",
        UserAgent = "tests",
        TotalTimeoutSeconds = 25,
        MaxResults = 10,
        PrimaryAuthorLookups = 2,
        RequestsPerSecond = requestsPerSecond,
        MaxQueuedRequests = maxQueued,
        CacheLifetimeMinutes = 45,
        CacheMaxEntries = 100
    };

    private static OpenLibraryRateLimitingHandler CreateHandler(
        OpenLibraryOptions options,
        HttpMessageHandler inner)
    {
        var handler = new OpenLibraryRateLimitingHandler(
            new OpenLibraryRateLimiter(Microsoft.Extensions.Options.Options.Create(options)),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenLibraryRateLimitingHandler>.Instance)
        {
            InnerHandler = inner
        };

        return handler;
    }

    /// <summary>
    /// The rate limit belongs to Open Library, so requests arriving concurrently from
    /// different callers must share one budget rather than each getting their own.
    /// </summary>
    [Fact]
    public async Task Paces_concurrent_requests_against_a_single_shared_budget()
    {
        const int RequestsPerSecond = 3;

        var counting = new CountingHandler();
        using var client = new HttpClient(CreateHandler(Options(RequestsPerSecond), counting))
        {
            BaseAddress = new Uri("https://openlibrary.test/")
        };

        // Twelve callers arriving at once, as separate inbound API requests would.
        await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => client.GetAsync("search.json")));

        Assert.Equal(12, counting.Count);

        // An average under the limit can still hide a burst, so assert the peak: no sliding
        // one-second window may contain more than the allowance.
        var sent = counting.Timestamps.OrderBy(timestamp => timestamp).ToArray();

        var peak = sent.Max(start =>
            sent.Count(t => t >= start && t - start < TimeSpan.FromSeconds(1)));

        Assert.True(
            peak <= RequestsPerSecond,
            $"a 1s window contained {peak} requests, over the {RequestsPerSecond}/s allowance.");
    }

    /// <summary>
    /// A full queue should be answered quickly rather than held until it times out.
    /// </summary>
    [Fact]
    public async Task Rejects_rather_than_queues_without_limit()
    {
        var counting = new CountingHandler();
        using var client = new HttpClient(CreateHandler(Options(requestsPerSecond: 1, maxQueued: 1), counting))
        {
            BaseAddress = new Uri("https://openlibrary.test/")
        };

        var attempts = Enumerable.Range(0, 10)
            .Select(async _ =>
            {
                try
                {
                    using var response = await client.GetAsync("search.json");
                    return null as Exception;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            });

        var outcomes = await Task.WhenAll(attempts);

        // Some were refused, and the refusal is the type the API already maps to a 502.
        var refusals = outcomes.OfType<OpenLibraryException>().ToArray();
        Assert.NotEmpty(refusals);
    }

    /// <summary>
    /// The handler is registered after the defaults so that it sits inside the resilience
    /// handler. If that ordering ever inverts, retries would bypass pacing entirely -- the
    /// exact moment pacing matters, since retries follow a 429.
    /// </summary>
    [Fact]
    public async Task Every_retry_attempt_takes_its_own_permit()
    {
        var counting = new CountingHandler(HttpStatusCode.ServiceUnavailable);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options()));
        services.AddSingleton<OpenLibraryRateLimiter>();
        services.AddTransient<OpenLibraryRateLimitingHandler>();

        // Mirrors ServiceDefaults: resilience applied to every client, before per-client handlers.
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        services.AddHttpClient("openlibrary", c => c.BaseAddress = new Uri("https://openlibrary.test/"))
            .AddHttpMessageHandler<OpenLibraryRateLimitingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => counting);

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("openlibrary");
        var limiter = provider.GetRequiredService<OpenLibraryRateLimiter>();

        using var response = await client.GetAsync("search.json");

        // The resilience handler retries a 503, so several attempts reach the wire. Every one
        // of them must have taken a permit; a permit count of 1 would mean only the first
        // attempt was paced and the retries went out unthrottled.
        Assert.True(counting.Count > 1, $"expected retries, saw {counting.Count} attempt(s)");
        Assert.Equal(counting.Count, limiter.PermitsGranted);
    }

    private sealed class CountingHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<TimeSpan> _timestamps = [];
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int _count;

        public int Count => _count;

        /// <summary>When each request reached the wire, measured from handler construction.</summary>
        public IReadOnlyCollection<TimeSpan> Timestamps => _timestamps;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            _timestamps.Add(_clock.Elapsed);

            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
