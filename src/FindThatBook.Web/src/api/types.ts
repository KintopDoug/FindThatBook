/**
 * Mirrors the API contract in FindThatBook.Api/Models. Kept hand-written rather than
 * generated so the shape stays readable, but it must move whenever the API does.
 */

/** Which path produced a result: the language model, or the built-in deterministic rules. */
export type ProcessingSource = 'Llm' | 'Fallback';

/** The structured reading of a raw query. Every field is optional: "dickens" yields only an author. */
export interface ExtractedQuery {
  title: string | null;
  author: string | null;
  keywords: string[];
}

export interface BookCandidate {
  title: string;
  authors: string[];
  firstPublishYear: number | null;
  openLibraryKey: string | null;
  openLibraryUrl: string | null;
  coverImageUrl: string | null;
  /** Short, grounded sentence naming the evidence behind the match. */
  explanation: string;
}

export interface BookSearchResponse {
  query: string;
  interpretation: ExtractedQuery;
  extractionSource: ProcessingSource;
  extractionFallbackReason: string | null;
  /** Null when there were no results to order, so no ranking ran. */
  rankingSource: ProcessingSource | null;
  rankingFallbackReason: string | null;
  results: BookCandidate[];
}

/**
 * RFC 9457 problem document, as produced by the API's GlobalExceptionHandler.
 * `traceId` correlates the failure with the server logs.
 */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  traceId?: string;
  /** Present outside production only. */
  exceptionType?: string;
  stackTrace?: string;
}
