import type { ProblemDetails } from './types';

/**
 * A failed API call, already reduced to something worth showing a user.
 *
 * The API answers every failure with an RFC 9457 problem document, so the useful text is
 * almost always in `detail`. `title` is the category ("Invalid query", "Book search is
 * unavailable"), and `traceId` ties the failure to the server logs.
 */
export class ApiError extends Error {
  readonly status: number | null;
  readonly title: string;
  readonly detail: string;
  readonly traceId: string | null;
  /** True when the request never reached the API at all. */
  readonly isNetworkError: boolean;
  readonly problem: ProblemDetails | null;

  constructor(init: {
    status?: number | null;
    title: string;
    detail: string;
    traceId?: string | null;
    isNetworkError?: boolean;
    problem?: ProblemDetails | null;
  }) {
    super(init.detail || init.title);
    this.name = 'ApiError';
    this.status = init.status ?? null;
    this.title = init.title;
    this.detail = init.detail;
    this.traceId = init.traceId ?? null;
    this.isNetworkError = init.isNetworkError ?? false;
    this.problem = init.problem ?? null;
  }

  /**
   * Whether the caller can fix this by changing what they typed. Drives whether the UI
   * asks the user to edit the query or to try again later.
   */
  get isUserFixable(): boolean {
    return this.status !== null && this.status >= 400 && this.status < 500;
  }

  /** Whether retrying the same query might plausibly work. */
  get isRetryable(): boolean {
    return this.isNetworkError || (this.status !== null && this.status >= 500);
  }
}
