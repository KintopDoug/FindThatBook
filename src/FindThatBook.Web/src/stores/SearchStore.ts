import { makeAutoObservable, runInAction } from 'mobx';
import { ApiError } from '../api/ApiError';
import type { BookSearchResponse, ProcessingSource } from '../api/types';
import { BookSearchService, bookSearchService } from '../services/bookSearchService';

export type SearchStatus = 'idle' | 'searching' | 'done' | 'failed';

/** A completed search, kept so the user can return to it without another API call. */
export interface HistoryEntry {
  /** The normalized query the API echoed back. */
  query: string;
  response: BookSearchResponse;
  searchedAt: number;
}

/** How many past searches stay available. Small on purpose: this is a way back, not an archive. */
const MAX_HISTORY = 5;

export class SearchStore {
  query = '';
  status: SearchStatus = 'idle';
  response: BookSearchResponse | null = null;
  error: ApiError | null = null;

  /** Completed searches from this session, newest first. */
  history: HistoryEntry[] = [];

  /**
   * True while the displayed response came from history rather than a fresh call. Shown in
   * the UI so a replayed answer is never mistaken for a new one.
   */
  viewingFromHistory = false;

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
      this.viewingFromHistory = false;
    });

    try {
      const response = await this.service.search(target, controller.signal);

      runInAction(() => {
        this.response = response;
        this.status = 'done';
        this.remember(response);
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

  /**
   * Records a completed search, newest first, replacing any earlier entry for the same
   * query so revisiting one does not push the others out.
   */
  private remember(response: BookSearchResponse) {
    const key = response.query.toLowerCase();

    this.history = [
      { query: response.query, response, searchedAt: Date.now() },
      ...this.history.filter((entry) => entry.query.toLowerCase() !== key),
    ].slice(0, MAX_HISTORY);
  }

  /**
   * Shows a past result immediately, with no API call.
   *
   * Any in-flight search is abandoned first: the user has asked for something else, and a
   * late response would otherwise overwrite what they just chose to look at.
   */
  showFromHistory(entry: HistoryEntry) {
    this.inFlight?.abort();
    this.inFlight = null;

    this.query = entry.query;
    this.response = entry.response;
    this.error = null;
    this.status = 'done';
    this.viewingFromHistory = true;
  }

  /** True when this entry is the one currently on screen. */
  isShowing(entry: HistoryEntry): boolean {
    return this.viewingFromHistory && this.response === entry.response;
  }

  clearHistory() {
    this.history = [];
    this.viewingFromHistory = false;
  }

  reset() {
    this.inFlight?.abort();
    this.inFlight = null;
    this.query = '';
    this.status = 'idle';
    this.response = null;
    this.error = null;
    this.history = [];
    this.viewingFromHistory = false;
  }
}

export const searchStore = new SearchStore();
