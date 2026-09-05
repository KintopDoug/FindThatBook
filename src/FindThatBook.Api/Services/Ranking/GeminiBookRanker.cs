using System.Net.Http.Json;
using System.Text.Json;
using FindThatBook.Api.Configuration;
using FindThatBook.Api.Exceptions;
using FindThatBook.Api.Models.DTO;
using Microsoft.Extensions.Options;
using Polly;

namespace FindThatBook.Api.Services.Ranking
{
    /// <summary>
    /// Asks Gemini to order retrieved candidates and explain each match.
    /// </summary>
    /// <remarks>
    /// The model re-ranks a list we retrieved; it never supplies books. It answers with the
    /// Open Library keys we gave it, and any key we did not send is discarded, so a
    /// hallucinated title cannot reach the user. Candidates the model leaves out are appended
    /// in their original order rather than dropped, so a lazy or truncated answer degrades the
    /// ordering instead of losing results.
    /// </remarks>
    public class GeminiBookRanker(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        ILogger<GeminiBookRanker> logger) : ILlmBookRanker
    {
        private readonly GeminiOptions _options = geminiOptions.Value;

        private static readonly JsonSerializerOptions ResponseJson = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>Cap on an explanation, so one verbose answer cannot dominate the response.</summary>
        private const int MaxExplanationLength = 240;

        private const string SystemInstruction =
            """
            You are ordering library search results for a patron.

            You will get the patron's request and a numbered list of candidate books that were
            already retrieved from a catalogue. Order the candidates from best to worst answer
            to the request, and explain each one.

            Rules:
            - Use only the candidates provided. Never invent a book, and never return a key
              that is not in the list.
            - Include every candidate exactly once.
            - Prefer a candidate whose author is marked as a confirmed primary author over one
              whose authors are unconfirmed, because unconfirmed names may be illustrators,
              editors, or adaptors rather than the writer.
            - Prefer the original work over an adaptation, abridgement, or study guide unless
              the request asks for one.
            - Each explanation must be one short sentence naming the evidence you actually
              used, such as the title matching or the author matching. Do not mention scores,
              rankings, or your own reasoning process. Do not state facts about a book that are
              not present in the candidate data.
            """;

        private static object ResponseSchema() => new
        {
            type = "OBJECT",
            properties = new
            {
                ranked = new
                {
                    type = "ARRAY",
                    items = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            key = new { type = "STRING" },
                            explanation = new { type = "STRING" }
                        },
                        required = new[] { "key", "explanation" }
                    }
                }
            },
            required = new[] { "ranked" }
        };

        public async Task<IReadOnlyList<RankedWork>> RankAsync(
            string rawQuery,
            ExtractedQuery query,
            IReadOnlyList<OpenLibraryWork> works,
            CancellationToken cancellationToken)
        {
            var request = new
            {
                systemInstruction = new { parts = new[] { new { text = SystemInstruction } } },
                contents = new[] { new { parts = new[] { new { text = BuildPrompt(rawQuery, query, works) } } } },
                generationConfig = new
                {
                    temperature = 0,
                    responseMimeType = "application/json",
                    responseSchema = ResponseSchema()
                }
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            HttpResponseMessage response;
            try
            {
                using var message = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"models/{_options.Model}:generateContent")
                {
                    Content = JsonContent.Create(request)
                };
                message.Headers.Add("x-goog-api-key", _options.ApiKey);

                response = await httpClient.SendAsync(message, timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ExecutionRejectedException exception)
            {
                throw new LlmRankingException("Gemini was unreachable or unresponsive.", exception);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                throw new LlmRankingException("Gemini request failed.", exception);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

                    logger.LogWarning(
                        "Gemini returned {StatusCode} for ranking. Body: {Body}",
                        (int)response.StatusCode,
                        body.Length <= 500 ? body : body[..500] + "...");

                    throw new LlmRankingException($"Gemini returned {(int)response.StatusCode}.");
                }

                var payload = await response.Content.ReadAsStringAsync(timeout.Token);

                return Reconcile(Parse(payload), works);
            }
        }

        /// <summary>
        /// Describes the candidates in a compact, stable form. Author confirmation is stated
        /// explicitly because it is the distinction the model is asked to weigh.
        /// </summary>
        private static string BuildPrompt(
            string rawQuery,
            ExtractedQuery query,
            IReadOnlyList<OpenLibraryWork> works)
        {
            var lines = works.Select((work, index) =>
            {
                var authors = work.BestKnownAuthors.Count > 0
                    ? string.Join(", ", work.BestKnownAuthors)
                    : "unknown";

                var confirmation = work.HasConfirmedPrimaryAuthors
                    ? "confirmed primary author"
                    : "unconfirmed, may include contributors";

                var year = work.FirstPublishYear?.ToString() ?? "unknown";

                return $"{index + 1}. key={work.Key} | title={work.Title}"
                    + (string.IsNullOrWhiteSpace(work.Subtitle) ? string.Empty : $": {work.Subtitle}")
                    + $" | authors={authors} ({confirmation})"
                    + $" | first published={year}";
            });

            return $"""
                Patron request: {rawQuery}

                Interpreted as:
                - title: {query.Title ?? "(none identified)"}
                - author: {query.Author ?? "(none identified)"}
                - keywords: {(query.Keywords.Count > 0 ? string.Join(", ", query.Keywords) : "(none)")}

                Candidates:
                {string.Join('\n', lines)}
                """;
        }

        private static IReadOnlyList<RankedEntry> Parse(string payload)
        {
            string text;
            try
            {
                using var document = JsonDocument.Parse(payload);

                text = document.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString() ?? string.Empty;
            }
            catch (Exception exception)
                when (exception is JsonException or KeyNotFoundException
                    or InvalidOperationException or IndexOutOfRangeException)
            {
                throw new LlmRankingException("Gemini response envelope was not readable.", exception);
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<RankingPayload>(text, ResponseJson);

                return parsed?.Ranked ?? throw new LlmRankingException("Gemini returned no ranking.");
            }
            catch (JsonException exception)
            {
                throw new LlmRankingException("Gemini returned content that was not valid JSON.", exception);
            }
        }

        /// <summary>
        /// Maps the model's answer back onto the works we actually retrieved.
        /// </summary>
        private IReadOnlyList<RankedWork> Reconcile(
            IReadOnlyList<RankedEntry> entries,
            IReadOnlyList<OpenLibraryWork> works)
        {
            var byKey = works.ToDictionary(work => work.Key, StringComparer.OrdinalIgnoreCase);
            var ranked = new List<RankedWork>(works.Count);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Key)
                    || !byKey.TryGetValue(entry.Key, out var work)
                    || !used.Add(entry.Key))
                {
                    // A key we never sent, or a duplicate. Either way it describes no real
                    // candidate, so it cannot be shown.
                    logger.LogWarning("Discarding ranked entry for unknown or repeated key {Key}.", entry.Key);
                    continue;
                }

                ranked.Add(new RankedWork
                {
                    Work = work,
                    Explanation = Clean(entry.Explanation)
                });
            }

            if (ranked.Count == 0)
            {
                throw new LlmRankingException("Gemini returned no usable ranking entries.");
            }

            // Anything the model skipped still belongs in the results, just lower down.
            ranked.AddRange(works
                .Where(work => !used.Contains(work.Key))
                .Select(work => new RankedWork
                {
                    Work = work,
                    Explanation = "Returned by Open Library for this search."
                }));

            return ranked;
        }

        private static string Clean(string? explanation)
        {
            var trimmed = explanation?.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                return "Returned by Open Library for this search.";
            }

            return trimmed.Length <= MaxExplanationLength
                ? trimmed
                : trimmed[..MaxExplanationLength].TrimEnd() + "...";
        }

        private sealed record RankedEntry(string? Key, string? Explanation);

        private sealed record RankingPayload(RankedEntry[]? Ranked);
    }
}
