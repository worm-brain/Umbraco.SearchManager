import { UMB_SETTINGS_SECTION_PATHNAME } from "@umbraco-cms/backoffice/settings";
import { UMB_WORKSPACE_VIEW_PATH_PATTERN } from "@umbraco-cms/backoffice/workspace";
import { SEARCH_MANAGER_ENTITY_TYPE } from "../workspace/constants.js";

/** The Search Manager views a link can open. */
export type SearchManagerView = "indexes" | "explore" | "diagnose";

/**
 * The query parameter that names the index a Search Manager view should open with. The Indexes
 * view opens that index's details sidebar; Explore and Diagnose select that index.
 */
export const INDEX_QUERY_PARAMETER = "index";

/**
 * The backoffice path of a Search Manager view, focused on one index.
 *
 * @param view - The view.
 * @param indexName - The index; `undefined` for a view that is not about one index (Diagnose's
 *   Find a node), when no `index` parameter is written.
 * @param extra - More query parameters for the view (Explore's chips, Diagnose's tool, ...).
 *   They follow `index` in order; an `index` among them is ignored, so `indexName` always wins.
 * @returns The path, for example `section/settings/workspace/search-manager/view/explore?index=Umb_Content`.
 */
export function searchManagerViewPath(
  view: SearchManagerView,
  indexName: string | undefined,
  extra?: URLSearchParams | Record<string, string>,
): string {
  const path = UMB_WORKSPACE_VIEW_PATH_PATTERN.generateAbsolute({
    sectionName: UMB_SETTINGS_SECTION_PATHNAME,
    entityType: SEARCH_MANAGER_ENTITY_TYPE,
    viewPathname: view,
  });
  const params = new URLSearchParams(indexName === undefined ? {} : { [INDEX_QUERY_PARAMETER]: indexName });
  for (const [key, value] of new URLSearchParams(extra)) {
    if (key !== INDEX_QUERY_PARAMETER) params.set(key, value);
  }
  const query = params.toString();
  return query ? `${path}?${query}` : path;
}

/**
 * Reads the index a view was opened for, from a location's query string.
 *
 * @param search - `location.search`.
 * @returns The index name, or undefined.
 */
export function indexFromQuery(search: string): string | undefined {
  return new URLSearchParams(search).get(INDEX_QUERY_PARAMETER) ?? undefined;
}
