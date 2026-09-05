import { ApiClient, apiClient } from '../api/ApiClient';
import { ApiError } from '../api/ApiError';
import type { BookSearchResponse } from '../api/types';

/**
 * The application's entry point into book searching.
 *
 * Sits between the UI and {@link ApiClient}: the UI hands it a raw query and gets back a
 * result, with no knowledge of HTTP. The one piece of judgement it applies is refusing an
 * empty query locally, because a round trip is not needed to know the answer.
 */
export class BookSearchService {
  private readonly client: ApiClient;

  constructor(client: ApiClient = apiClient) {
    this.client = client;
  }

  /**
   * Searches for books matching a raw, messy query.
   *
   * @param query Whatever the user typed: title, author, keywords, or any mix.
   * @param signal Cancels a search that has been superseded.
   * @throws {ApiError} When the query is unusable, or the API reports a failure.
   */
  async search(query: string, signal?: AbortSignal): Promise<BookSearchResponse> {
    const trimmed = query.trim();

    if (trimmed.length === 0) {
      // The API would answer this with a 400 saying the same thing. Answering locally keeps
      // an empty submission from costing a request and a spinner.
      throw new ApiError({
        status: 400,
        title: 'Invalid query',
        detail: 'Enter a title, an author, or a few keywords to search for.',
      });
    }

    return this.client.searchBooks(trimmed, signal);
  }
}

export const bookSearchService = new BookSearchService();
