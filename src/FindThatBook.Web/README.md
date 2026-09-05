# Find That Book — Web

React + TypeScript UI for the Find That Book API, using MUI for components and MobX for
state.

## Running

### With Aspire (recommended)

From the repository root:

```bash
dotnet run --project src/FindThatBook.AppHost
```

That is the only command needed from a fresh clone. The AppHost installs npm dependencies
first (as a `web-npm-install` resource), waits for the API to report healthy, then starts the
dev server. The dashboard lists both resources; open the `web` one. Its port is allocated by
Aspire, so it changes between runs.

### Standalone

```bash
npm install
npm run dev
```

Served at <http://localhost:5173>. The API has to be running separately; the proxy falls back
to `https://localhost:7082`, its HTTPS launch profile.

## How the API address is resolved

`ApiClient`'s base URL is empty, so requests go to this app's own origin and the Vite dev
server proxies `/api` onward. The browser stays same-origin, which is why the API needs no
CORS policy.

The proxy target is chosen in priority order:

1. **Aspire service discovery.** `.WithReference(api)` in the AppHost injects the API's
   allocated endpoints as `services__api__{scheme}__{index}`. `vite.config.ts` reads those,
   preferring HTTPS because the API redirects HTTP to it. Aspire assigns the ports, so this
   is the only way to stay correct across runs.
2. `VITE_API_PROXY_TARGET`, for an API the AppHost did not start.
3. The HTTPS launch profile, for standalone use.

On startup the dev server logs which one it used, visible in the dashboard's console logs for
the `web` resource:

```
[proxy] /api -> https://localhost:7082 (via Aspire service discovery)
```

See `.env.example` for the environment variables.

## Layout

| Path | Responsibility |
| --- | --- |
| `src/api/ApiClient.ts` | Executes HTTP requests and owns API configuration. Turns failures into `ApiError`. |
| `src/api/ApiError.ts` | A failed call reduced to something displayable, parsed from the API's problem document. |
| `src/api/apiConfig.ts` | Base URL and timeout, read from environment variables. |
| `src/api/types.ts` | Hand-written mirror of the API contract. |
| `src/services/bookSearchService.ts` | Consumes `ApiClient`; exposes `search(query)` to the UI. |
| `src/stores/SearchStore.ts` | MobX store: query, status, results, errors, fallback state. |
| `src/components/` | Presentation only. |

The layering is deliberate: nothing above `ApiClient` sees a `Response` or a status code, and
nothing above `bookSearchService` knows that HTTP is involved at all.

## Behaviour worth knowing

**Errors.** The API answers every failure with an RFC 9457 problem document, so one component
renders all of them. A 4xx is framed as something the reader can fix by editing the query; a
5xx offers a retry. The `traceId` is always shown, because it is what ties the failure to the
server logs.

**AI vs built-in rules.** The API reports separately whether *interpretation* and *ranking*
came from the language model or the deterministic fallback. Both are shown as chips, and if
either fell back, a disclaimer explains that results may be less accurate and repeats the
reason the API gave. Without a Gemini API key configured, every search uses the fallback.

**Ranking.** Results are ordered best-first with the position shown as a numbered badge. The
API deliberately returns no numeric score, so none is displayed — position and the grounded
explanation carry the strength of the match.

## Scripts

```bash
npm run dev      # dev server with API proxy
npm run build    # type-check and production build
npm run preview  # serve the production build
```
