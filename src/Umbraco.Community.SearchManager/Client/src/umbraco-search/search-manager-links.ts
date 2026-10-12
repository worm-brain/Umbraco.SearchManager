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
 * @param indexName - The index.
 * @returns The path, for example `section/settings/workspace/search-manager/view/explore?index=Umb_Content`.
 */
export function searchManagerViewPath(view: SearchManagerView, indexName: string): string {
  const path = UMB_WORKSPACE_VIEW_PATH_PATTERN.generateAbsolute({
    sectionName: UMB_SETTINGS_SECTION_PATHNAME,
    entityType: SEARCH_MANAGER_ENTITY_TYPE,
    viewPathname: view,
  });
  return `${path}?${new URLSearchParams({ [INDEX_QUERY_PARAMETER]: indexName })}`;
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
