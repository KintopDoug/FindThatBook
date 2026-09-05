import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

/**
 * The dev server proxies /api to the backend, so the browser only ever talks to its own
 * origin. That is why the API needs no CORS policy, and why ApiClient's default base URL is
 * empty.
 *
 * The proxy target is resolved in priority order:
 *
 *  1. Aspire service discovery. When the AppHost runs this app with `.WithReference(api)`,
 *     it injects the API's allocated endpoints as `services__api__{scheme}__{index}`. Since
 *     Aspire assigns those ports, reading them is the only way to stay correct across runs.
 *  2. VITE_API_PROXY_TARGET, for pointing at an API the AppHost did not start.
 *  3. The API's HTTPS launch profile, for running `npm run dev` on its own.
 */
function resolveApiTarget(): string {
  const discovered = discoverFromAspire();

  if (discovered) {
    return discovered;
  }

  return process.env.VITE_API_PROXY_TARGET ?? 'https://localhost:7082';
}

/**
 * Reads Aspire's injected endpoints for the "api" resource. HTTPS is preferred because the
 * API redirects HTTP to it, and a proxied 307 is awkward to follow.
 */
function discoverFromAspire(): string | undefined {
  const named = (scheme: string) =>
    Object.keys(process.env)
      .filter((key) => key.startsWith(`services__api__${scheme}__`))
      .sort()
      .map((key) => process.env[key])
      .find((value): value is string => Boolean(value));

  return named('https') ?? named('http');
}

const discovered = discoverFromAspire();
const apiTarget = resolveApiTarget();

// Printed on startup so it is obvious from the Aspire dashboard's console logs which API a
// running dev server is actually talking to, and whether discovery worked.
console.log(
  `[proxy] /api -> ${apiTarget} (${discovered ? 'via Aspire service discovery' : 'configured fallback'})`,
);

// Aspire allocates the port and passes it as PORT. Falling back to Vite's default keeps
// standalone `npm run dev` working.
const port = Number(process.env.PORT) || 5173;

export default defineConfig({
  plugins: [react()],
  server: {
    port,
    // Fail loudly rather than drifting to another port: the AppHost has already published
    // this one as the resource's endpoint, so a silent move would break the dashboard link.
    strictPort: true,
    proxy: {
      '/api': {
        target: apiTarget,
        changeOrigin: true,
        // Accepts the ASP.NET development certificate.
        secure: false,
      },
    },
  },
});
