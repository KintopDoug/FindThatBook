/**
 * Where the API lives, and how long we are willing to wait for it.
 *
 * The default base URL is empty, meaning requests go to the current origin and Vite's dev
 * proxy forwards them. That keeps the browser same-origin, so the API needs no CORS policy
 * for local development. Point VITE_API_BASE_URL at an absolute URL to talk to a deployed
 * API instead.
 */
export interface ApiConfig {
  baseUrl: string;
  /**
   * Client-side ceiling on one request. Deliberately longer than the server's own budget:
   * a search can involve an LLM call plus several rate-paced catalogue requests, and cutting
   * it off early would report a timeout for work that was about to succeed.
   */
  timeoutMs: number;
}

const DEFAULT_TIMEOUT_MS = 60_000;

function readTimeout(): number {
  const configured = Number(import.meta.env.VITE_API_TIMEOUT_MS);

  return Number.isFinite(configured) && configured > 0 ? configured : DEFAULT_TIMEOUT_MS;
}

export const apiConfig: ApiConfig = {
  baseUrl: (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, ''),
  timeoutMs: readTimeout(),
};
