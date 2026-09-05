import { makeAutoObservable, runInAction } from 'mobx';
import { ApiError } from '../api/ApiError';
import type { BookSearchResponse, ProcessingSource } from '../api/types';
import { BookSearchService, bookSearchService } from '../services/bookSearchService';

export type SearchStatus = 'idle' | 'searching' | 'done' | 'failed';

export class SearchStore {
  query = '';
  status: SearchStatus = 'idle';
  response: BookSearchResponse | null = null;
  error: ApiError | null = null;

  /** Cancels the in-flight search when a newer one starts. */
  private inFlight: AbortController | null = null;

  private readonly service: BookSearchService;

  constructor(service: BookSearchService = bookSearchService) {
    this.service = service;
    makeAutoObservable(this, {}, { autoBind: true });
  }

  setQuery(value: string) {
    this.query = value;
  }

  get isSearching(): boolean {
    return this.status === 'searching';
  }

  get hasSearched(): boolean {
    return this.status === 'done' || this.status === 'failed';
  }

  /** A search that succeeded but matched nothing, which is a normal outcome rather than an error. */
  get foundNothing(): boolean {
    return this.status === 'done' && this.response !== null && this.response.results.length === 0;
  }

  /**
   * True when either stage fell back to deterministic rules, meaning results are likely to
   * be less accurate than usual. Drives the disclaimer.
   */
  get usedFallback(): boolean {
    if (!this.response) return false;

    return (
      this.response.extractionSource === 'Fallback' || this.response.rankingSource === 'Fallback'
    );
  }

  /** The reasons the API gave, deduplicated: both stages often fall back for the same cause. */
  get fallbackReasons(): string[] {
    if (!this.response) return [];

    const reasons = [
      this.response.extractionFallbackReason,
      this.response.rankingFallbackReason,
    ].filter((reason): reason is string => Boolean(reason));

    return [...new Set(reasons)];
  }

  get extractionSource(): ProcessingSource | null {
    return this.response?.extractionSource ?? null;
  }

  get rankingSource(): ProcessingSource | null {
    return this.response?.rankingSource ?? null;
  }

  async search(query?: string) {
    const target = query ?? this.query;

    // Abandon any older search so its response cannot arrive late and overwrite this one.
    this.inFlight?.abort();
    const controller = new AbortController();
    this.inFlight = controller;

    runInAction(() => {
      this.query = target;
      this.status = 'searching';
      this.error = null;
    });

    try {
      const response = await this.service.search(target, controller.signal);

      runInAction(() => {
        this.response = response;
        this.status = 'done';
      });
    } catch (error) {
      // A superseded search is not a failure: a newer one is already running, and showing
      // an error for work we chose to abandon would be noise.
      if (controller.signal.aborted && !(error instanceof ApiError)) {
        return;
      }

      runInAction(() => {
        this.error =
          error instanceof ApiError
            ? error
            : new ApiError({
                title: 'Something went wrong',
                detail: 'The search could not be completed.',
              });
        this.status = 'failed';
        this.response = null;
      });
    } finally {
      if (this.inFlight === controller) {
        this.inFlight = null;
      }
    }
  }

  reset() {
    this.inFlight?.abort();
    this.inFlight = null;
    this.query = '';
    this.status = 'idle';
    this.response = null;
    this.error = null;
  }
}

export const searchStore = new SearchStore();
