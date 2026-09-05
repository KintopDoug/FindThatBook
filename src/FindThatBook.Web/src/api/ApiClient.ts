import { ApiError } from './ApiError';
import { apiConfig, type ApiConfig } from './apiConfig';
import type { BookSearchResponse, ProblemDetails } from './types';

/**
 * Executes HTTP requests against the Find That Book API and owns its configuration.
 *
 * Everything that knows about transport lives here: base URL, timeouts, query-string
 * building, and turning a failure response into an {@link ApiError}. Callers above this
 * layer never see a Response object or a status code.
 */
export class ApiClient {
  private readonly config: ApiConfig;

  constructor(config: ApiConfig = apiConfig) {
    this.config = config;
  }

  searchBooks(query: string, signal?: AbortSignal): Promise<BookSearchResponse> {
    return this.get<BookSearchResponse>('/api/search', { query }, signal);
  }

  private async get<T>(
    path: string,
    params: Record<string, string>,
    signal?: AbortSignal,
  ): Promise<T> {
    const url = this.buildUrl(path, params);

    // Own timeout, combined with any caller cancellation, so a superseded search can be
    // abandoned without waiting for the timeout to expire.
    const timeout = AbortSignal.timeout(this.config.timeoutMs);
    const combined = signal ? AbortSignal.any([signal, timeout]) : timeout;

    let response: Response;
    try {
      response = await fetch(url, {
        method: 'GET',
        headers: { Accept: 'application/json' },
        signal: combined,
      });
    } catch (error) {
      // A caller-initiated abort is not a failure worth reporting; let it propagate so the
      // store can ignore it rather than rendering an error for a search nobody wants.
      if (signal?.aborted) {
        throw error;
      }

      throw ApiClient.toTransportError(error, this.config.timeoutMs);
    }

    if (!response.ok) {
      throw await ApiClient.toApiError(response);
    }

    return (await response.json()) as T;
  }

  private buildUrl(path: string, params: Record<string, string>): string {
    const search = new URLSearchParams(params).toString();

    return `${this.config.baseUrl}${path}${search ? `?${search}` : ''}`;
  }

  /** The request never reached the API, so there is no problem document to read. */
  private static toTransportError(error: unknown, timeoutMs: number): ApiError {
    const timedOut = error instanceof DOMException && error.name === 'TimeoutError';

    return new ApiError({
      status: null,
      isNetworkError: true,
      title: timedOut ? 'The search took too long' : 'Could not reach the service',
      detail: timedOut
        ? `The API did not respond within ${Math.round(timeoutMs / 1000)} seconds.`
        : 'The API could not be reached. It may be starting up, or your connection may be down.',
    });
  }

  /**
   * Reads the API's problem document. Falls back to the status line when the body is
   * missing or unreadable, which is what a proxy or gateway failure looks like.
   */
  private static async toApiError(response: Response): Promise<ApiError> {
    let problem: ProblemDetails | null = null;

    try {
      const body = await response.text();

      if (body) {
        problem = JSON.parse(body) as ProblemDetails;
      }
    } catch {
      problem = null;
    }

    return new ApiError({
      status: response.status,
      problem,
      title: problem?.title?.trim() || ApiClient.titleForStatus(response.status),
      detail: problem?.detail?.trim() || ApiClient.detailForStatus(response.status),
      traceId: problem?.traceId ?? null,
    });
  }

  private static titleForStatus(status: number): string {
    if (status === 404) return 'Not found';
    if (status >= 500) return 'The service is unavailable';
    if (status >= 400) return 'That request could not be processed';

    return 'Something went wrong';
  }

  /**
   * Used when there is no problem document to read. A bodyless 5xx usually means the request
   * died before reaching the API -- a dev proxy with nothing behind it, or a gateway -- so
   * the wording points at that rather than blaming the search.
   */
  private static detailForStatus(status: number): string {
    if (status === 404) {
      return 'That endpoint does not exist. The app may be pointed at the wrong API URL.';
    }

    if (status === 502 || status === 503 || status === 504) {
      return 'The API could not be reached. Check that it is running, then try again.';
    }

    if (status >= 500) {
      return 'The service failed while handling the search. Trying again may work.';
    }

    return 'The service rejected the request but did not explain why.';
  }
}

/** Shared instance. Construct your own with a different config for tests. */
export const apiClient = new ApiClient();
