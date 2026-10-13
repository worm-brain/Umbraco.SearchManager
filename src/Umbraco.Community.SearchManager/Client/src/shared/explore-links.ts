import type { Filter } from "../api/index.js";
import { createExploreState, encodeExploreState } from "../explore/explore-state.js";
import { searchManagerViewPath } from "../umbraco-search/search-manager-links.js";

/** What an Explore link opens with, beyond the target. */
export interface ExploreLinkOptions {
  /** The chips. */
  filters?: Array<Filter>;
  /** The culture; Umbraco Search finds only invariant documents without one. */
  culture?: string;
  /** The id of the document to open in the drawer. */
  doc?: string;
}

/**
 * The backoffice path of the Explore view on a target, with chips, a culture or an open
 * document, in the same query-string form Explore writes itself (`encodeExploreState`), so the
 * view opens exactly as if the user had built it.
 *
 * @param target - The index or searcher.
 * @param options - The chips, culture and open document.
 * @returns The path, for example `section/settings/workspace/search-manager/view/explore?index=ExternalIndex&f=...`.
 */
export function exploreUrl(target: string, options: ExploreLinkOptions = {}): string {
  const params = encodeExploreState({
    ...createExploreState(),
    culture: options.culture || undefined,
    filters: options.filters ?? [],
    doc: options.doc || undefined,
  });
  return searchManagerViewPath("explore", target, params);
}

/**
 * The backoffice path of the Diagnose view's field statistics for an index. The tool is named
 * even though it is the default, so the link keeps opening field statistics if the default moves.
 *
 * @param index - The index.
 * @returns The path, for example `section/settings/workspace/search-manager/view/diagnose?index=ExternalIndex&tool=fields`.
 */
export function fieldStatisticsUrl(index: string): string {
  return searchManagerViewPath("diagnose", index, { tool: "fields" });
}
