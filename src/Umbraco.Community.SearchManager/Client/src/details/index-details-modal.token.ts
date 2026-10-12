import { UmbModalToken } from "@umbraco-cms/backoffice/modal";
import type { IndexSummary } from "../api/types.gen.js";

/** Alias of the index details sidebar. */
export const SEARCH_MANAGER_INDEX_DETAILS_MODAL_ALIAS = "Umbraco.Community.SearchManager.Modal.IndexDetails";

/** What the details sidebar shows. */
export interface IndexDetailsModalData {
  /** The index, as the Indexes view has it (its provider properties included). */
  index: IndexSummary;
}

/** Opens the index details sidebar: rebuild history and provider properties. */
export const SEARCH_MANAGER_INDEX_DETAILS_MODAL = new UmbModalToken<IndexDetailsModalData, never>(
  SEARCH_MANAGER_INDEX_DETAILS_MODAL_ALIAS,
  { modal: { type: "sidebar", size: "medium" } },
);
