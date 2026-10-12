import type { Filter } from "../api/index.js";
import { isFilter } from "./filters.js";

/** Results per page in the Explore view. */
export const PAGE_SIZE = 20;

/**
 * Everything that defines what the Explore view shows, kept in the URL so a link reopens it.
 */
export interface ExploreViewState {
  /** The index or searcher; `undefined` picks the first one listed. */
  target?: string;
  /** The culture whose variant fields are searched; `undefined` is all cultures. */
  culture?: string;
  /** The chips, disabled ones included. */
  filters: Array<Filter>;
  /** Extra result columns: field names. */
  columns: Array<string>;
  /** The id of the document open in the drawer. */
  doc?: string;
  /** The page, from 1. */
  page: number;
  /**
   * The raw Lucene query in native mode; `undefined` is chip mode, and any string, `""`
   * included, is native mode.
   */
  native?: string;
  /** Whether the show-query panel is open. */
  showQuery?: boolean;
}

/** The query-string keys the view owns; other keys in the URL are kept as they are. */
export const EXPLORE_STATE_KEYS = ["index", "culture", "f", "cols", "doc", "page", "native", "sq"] as const;

/**
 * An empty view: first target, all cultures, no chips.
 *
 * @returns A fresh state.
 */
export function createExploreState(): ExploreViewState {
  return { filters: [], columns: [], page: 1 };
}

/**
 * Writes the state as query-string parameters, leaving out defaults. Chips are base64url JSON
 * (`f`), columns a comma list (`cols`); `native` is written even when empty, because an empty
 * native query is still native mode; `sq=1` keeps the show-query panel open.
 *
 * @param state - The state.
 * @returns The parameters, in a stable order.
 */
export function encodeExploreState(state: ExploreViewState): URLSearchParams {
  const params = new URLSearchParams();
  if (state.target) params.set("index", state.target);
  if (state.culture) params.set("culture", state.culture);
  if (state.filters.length > 0) params.set("f", toBase64Url(JSON.stringify(state.filters)));
  if (state.columns.length > 0) params.set("cols", state.columns.join(","));
  if (state.doc) params.set("doc", state.doc);
  if (state.page > 1) params.set("page", String(state.page));
  if (state.native !== undefined) params.set("native", state.native);
  if (state.showQuery) params.set("sq", "1");
  return params;
}

/**
 * Reads the state from query-string parameters. Never throws: a missing or broken parameter
 * falls back to its default on its own, so one bad value does not discard a shared link.
 *
 * @param params - The query string.
 * @returns The state.
 */
export function decodeExploreState(params: URLSearchParams): ExploreViewState {
  const page = Number(params.get("page"));
  return {
    target: params.get("index") || undefined,
    culture: params.get("culture") || undefined,
    filters: decodeFilters(params.get("f")),
    columns: (params.get("cols") ?? "")
      .split(",")
      .map((column) => column.trim())
      .filter((column) => column.length > 0),
    doc: params.get("doc") || undefined,
    page: Number.isInteger(page) && page > 1 ? page : 1,
    native: params.get("native") ?? undefined,
    showQuery: params.get("sq") === "1" ? true : undefined,
  };
}

/**
 * Replaces the view's keys in a query string and keeps every other key.
 *
 * @param search - The current query string, with or without `?`.
 * @param state - The encoded state.
 * @returns The merged query string without `?`.
 */
export function mergeIntoSearch(search: string, state: URLSearchParams): string {
  const merged = new URLSearchParams(search);
  for (const key of EXPLORE_STATE_KEYS) merged.delete(key);
  for (const [key, value] of state) merged.set(key, value);
  return merged.toString();
}

function decodeFilters(value: string | null): Array<Filter> {
  if (!value) return [];
  try {
    const parsed: unknown = JSON.parse(fromBase64Url(value));
    return Array.isArray(parsed) ? parsed.filter(isFilter) : [];
  } catch {
    // Not base64url, UTF-8 or JSON: the chips are lost, the rest of the link still applies.
    return [];
  }
}

function toBase64Url(text: string): string {
  let binary = "";
  for (const byte of new TextEncoder().encode(text)) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/** @throws When the input is not base64url or not UTF-8. */
function fromBase64Url(value: string): string {
  const binary = atob(value.replace(/-/g, "+").replace(/_/g, "/"));
  const bytes = Uint8Array.from(binary, (char) => char.charCodeAt(0));
  return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
}
