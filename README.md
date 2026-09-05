# Find That Book

Turns a messy, half-remembered description of a book into a ranked list of candidates from
Open Library, each with a short explanation of why it matched.

A .NET 10 Web API orchestrated by .NET Aspire, with a React front end.

---

## Getting started

### System requirements

| Requirement | Version used | Notes                                                          |
| ----------- | ------------ | -------------------------------------------------------------- |
| .NET SDK    | 10.0.400     | The Aspire AppHost targets `net10.0`.                          |
| Aspire CLI  | 13.5.3       | Installed with `dotnet tool install -g aspire.cli` if missing. |
| Node.js     | 24.x         | Needed for the React app; the AppHost starts it.               |
| npm         | 11.x         | Ships with Node.                                               |

An internet connection is required: the app calls Open Library, and Gemini when configured.
No database, container runtime, or other local infrastructure is needed.

On first run, trust the ASP.NET development certificate so the browser and the Vite dev proxy
accept the API's HTTPS endpoint:

```bash
dotnet dev-certs https --trust
```

### Running it

Run the AppHost project from visual studio.

Alternatively:
One command from the repository root:

```bash
dotnet run --project src/FindThatBook.AppHost
```

The AppHost installs npm packages, waits for the API to report healthy, then starts the Vite
dev server. The Aspire dashboard opens automatically and lists both resources:

- **api** — the Web API, with a link to Swagger UI
- **web** — the React app; open this one to use the product

Ports are allocated by Aspire and change between runs, so use the dashboard links rather than
bookmarking a port.

### Configuring the Gemini API key

**The app runs without a key.** Both AI stages fall back to deterministic rules, and the UI reports to the user when the fallback is used. Configure a key to enable the AI path.

Get one for gemini from [Google AI Studio](https://aistudio.google.com/apikey), then store it in user
secrets so it never reaches source control:

```bash
dotnet user-secrets set "Gemini:ApiKey" "YOUR_KEY_HERE" --project src/FindThatBook.Api
```

Or set an environment variable instead:

```bash
# bash
export Gemini__ApiKey="YOUR_KEY_HERE"
```

```powershell
# PowerShell
$env:Gemini__ApiKey = "YOUR_KEY_HERE"
```

Restart the AppHost. The chips in the UI flip from **Built-in rules** to **AI**, and the
reduced-accuracy disclaimer disappears.

The model, endpoint, and timeout live in `src/FindThatBook.Api/appsettings.json` under
`Gemini`. Only the key is secret.

### Running the tests

```bash
dotnet test
```

89 tests. See [Testing strategy](#testing-strategy) for what they cover.

---

## Features completed

**Query handling**

- Sparse queries ("dickens", "tale two cities"), noisy queries ("find me a book about
  whales"), and mixed title/author phrasing.
- Ingress normalization: Unicode NFC composition, invisible-character stripping, trimming,
  whitespace collapsing. Case, punctuation, and accents preserved as evidence.
- Validation with a configurable length limit, returning a specific reason rather than a
  generic rejection.

**AI**

- Gemini-backed interpretation of a raw query into title, author, and keywords, using a fixed
  JSON response schema at `temperature = 0`.
- Gemini-backed re-ranking of retrieved candidates, with a grounded one-sentence explanation
  per result.
- A deterministic fallback for each stage, so the app is fully usable with no API key.
- The path taken and the reason are reported on every response and surfaced in the UI.

**Open Library**

- Works search across title, author, and general-keyword fields.
- One widening retry when a field search returns nothing.
- Canonical work-record lookups to separate primary authors from contributors.
- De-duplication by work key.

**Ranking**

- Deterministic scoring on title and author evidence, with comparison-time folding for case,
  accents, punctuation, subtitles, and surnames.
- Three-state author evidence: confirmed primary author, confirmed contributor-only, and not
  yet checked — each scored and explained differently.
- Tie-breaking toward the more widely published and earlier work.

**API**

- .NET 10 Web API with Swagger UI and a typed OpenAPI document.
- Global exception handling returning RFC 9457 problem documents, mapping upstream failures
  to `502`/`504` and invalid input to `400`, each with a `traceId`.
- Options bound from configuration and validated at startup, so a bad value fails fast.

**Production concerns**

- 45-minute in-process result cache keyed on the interpreted query.
- Shared token-bucket rate limiter keeping outbound traffic within Open Library's 3 req/s
  allowance, applied inside the retry handler so retries are paced too.
- OpenTelemetry logs, traces, and metrics via Aspire ServiceDefaults.

**UI**

- React, TypeScript, MUI, MobX. Search box with example queries, loading skeletons, and
  cancellation of superseded searches.
- Results showing rank, cover, authors, first publish year, Open Library link, and the
  explanation behind each match.
- AI-vs-fallback chips per stage, with a reduced-accuracy disclaimer when either fell back.
- Error states rendered from the problem document, distinguishing what the user can fix from
  what they can only retry.

**Orchestration**

- Aspire AppHost running both projects, installing npm packages and waiting for the API to
  report healthy before starting the UI.
- The UI resolves the API address through Aspire service discovery rather than configuration.

---

## Testing strategy

89 xUnit tests, all unit-level and offline. External dependencies are replaced with fake
`HttpMessageHandler`s and hand-written stubs rather than a mocking library, which keeps the
test doubles readable and the dependency list short.

The suite targets the places where being wrong is either likely or expensive, rather than
chasing coverage:

| Area | Cases | What it protects |
| --- | --- | --- |
| Retrieval, caching, Open Library client | 25 | Search widening; primary-author confirmation; cache hits costing no requests; failures never being cached; resilience rejections mapping to `502` rather than `500` |
| Deterministic ranking and fallback parsing | 20 | Confirmed authors outranking contributors; comparison folding across case, accents, punctuation, subtitles; keyword-only queries matching author names |
| Query normalization and validation | 18 | Invisible-only queries counting as content; NFC composition; the length limit applying to real characters, not padding |
| Gemini clients (extraction and ranking) | 14 | Reading the response envelope; every unusable answer becoming one exception type; keys the model invented being discarded; omitted candidates appended rather than dropped; the API key travelling as a header, never in the URL |
| Fallback orchestration | 9 | Each degradation path reporting its reason; caller cancellation not being masked as a model failure |
| Rate limiting | 3 | No 1-second window exceeding the allowance; retries taking their own permit |

Two habits are worth calling out. **Assertions target behaviour, not implementation** — the
ranking tests assert the resulting order and explanation text, so scoring weights can be
tuned without rewriting tests. And **tests were mutation-checked**: deliberately breaking the
cache, the NFC step, and the rate limiter each failed exactly the tests that should have
failed. That check caught a real defect — a sliding-window limiter that allowed six requests
in one second against a limit of three, which an average-rate assertion would have missed.

---

## How a search works

The pipeline is five ordered stages, coordinated by `BookSearchService`. Each stage lives in
its own folder under `src/FindThatBook.Api/Services/`.

The division of labour is the central design decision: **the LLM is used only where the
answer is a matter of judgement.** when configured, the LLM Parses the raw user query into a format that we can use to query the OpenLibrary api, and then we query the LLM again to rank the candidates returned from Open Library to rank candidates based on the best match regarding several weighted reasoning methods.

### 1. Validation

`Validation/QueryValidationService` normalizes then validates, in that order.

Normalization composes Unicode to NFC, strips invisible characters, trims, and collapses
whitespace runs. Case, punctuation, and accents are deliberately **preserved** — they are
evidence for the next stage. Validation then rejects an empty query, or one exceeding
`Search:MaxQueryLength`, as a `400`.

Ordering matters: the length limit applies to real content, so padding cannot consume the
caller's budget.

### 2. Interpretation (AI)

`Extraction/QueryExtractionService` splits the query into title, author, and keywords.

Gemini is asked for JSON matching a fixed schema at `temperature = 0`. If no key is
configured, or the call fails, or the model returns nothing usable, a deterministic parser
takes over: it trusts a quoted phrase as a title and text after "by" as an author, and
otherwise declines to guess, letting the words fall through as keywords.

Which path ran, and why, travels back on the response.

### 3. Retrieval

`Retrieval/BookRetrievalService` queries Open Library.

It searches as specifically as the interpretation allows — title and author fields when
available, keywords otherwise. A field search that finds nothing is retried once as a broad
query, so a mislabelled title does not produce an empty page.

It then reads the canonical work record for the top candidates to identify **primary
authors**, because search results list illustrators, editors, and adaptors in the same field
as the writer.

Results are cached for 45 minutes and outbound requests are paced (see Tradeoffs).

### 4. Ranking (AI)

`Ranking/BookRankingService` orders the candidates and explains each one.

Gemini re-ranks the retrieved list, answering with the Open Library keys it was given. Keys
we never sent are discarded and omitted candidates are appended rather than dropped, so a bad
answer costs ordering quality, never correctness.

Without a key, `FallbackBookRanker` scores title and author evidence using comparison-time
normalization — case folding, accent stripping, subtitle handling, surname matching. A
confirmed primary author is rewarded; a name the catalogue says is only a contributor is
penalised; a name never checked is treated neutrally.

### 5. Presentation

`Mapping/BookCandidateMapper` builds the response: title, authors, first publish year, Open
Library link, cover image, and the explanation written by whichever ranker ran.

Explanations are never reconstructed here — they come from the stage that actually made the
decision, so the text always reflects the evidence used.

---

## Tradeoffs

**The LLM never chooses the books.** It interprets a query and reorders a list we retrieved.
This costs some ranking nuance, but builds a system that cannot hallucinate a title.

**Every AI stage degrades instead of failing.** Both fall back to deterministic rules and say
so on the response, with the reason. A reviewer can run the whole product with no API key. The
cost is two code paths per stage; the alternative — an app that is unusable without a key —
was worse.

**Outbound requests are paced and cached.** Open Library allows 3 requests/second for
identified clients. A shared token-bucket limiter spaces requests evenly rather than allowing
bursts, and a 45-minute cache means repeat searches cost nothing. This adds latency to a cold
search — roughly one second for three spaced calls — in exchange for not being throttled.

**Primary-author lookups are capped at 2.** Each costs an extra request. Confirming every
candidate would be more accurate and much slower, so most candidates are honestly reported as
"not verified" rather than assumed either way.

**Cache and rate limiter are in-process.** Simple and correct for one instance; both would
need to be distributed before scaling out, since 2 instances of the api would enable twice the number of requests allowed per second since each instance monitors only its own requests.

---

## What I would do next

**Scalability**

- Move the cache and rate limiter to Redis so limits hold across instances. Today two
  instances would each pace to 3 req/s and collectively exceed the rate limit enforced by open library.
- Parallelise the primary-author lookups under the rate limiter instead of running them
  sequentially.

**Accuracy**

- Cross-edition de-duplication. Open Library returns several records for the same work, and
  they currently occupy separate result slots.
- Fuzzy title matching for misspellings, so the deterministic ranker degrades more gracefully
  than exact-and-substring comparison allows.

**Function**

- Integration tests over the full pipeline with a stubbed Open Library; every test today is
  unit-level.
- numeric input enabling the user to set number of results returned
- Persist recent searches so a user can return to a result.
- Deploy it. The Aspire AppHost already describes the topology, so this is mostly a hosting
  decision.

---

## Project layout

```
src/
  FindThatBook.AppHost/          Aspire orchestration: API + web, service discovery
  FindThatBook.ServiceDefaults/  OpenTelemetry, health checks, resilience
  FindThatBook.Api/              Web API and the search pipeline
    Services/Validation|Extraction|Retrieval|Ranking|Mapping
  FindThatBook.Web/              React + TypeScript + MUI + MobX UI
tests/
  FindThatBook.Api.Tests/        xUnit
```

Book data from [Open Library](https://openlibrary.org). See
[`src/FindThatBook.Web/README.md`](src/FindThatBook.Web/README.md) for UI-specific detail.
