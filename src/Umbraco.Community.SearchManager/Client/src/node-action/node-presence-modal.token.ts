import { UmbModalToken } from "@umbraco-cms/backoffice/modal";

/** Alias of the "Show in search indexes" sidebar. */
export const SEARCH_MANAGER_NODE_PRESENCE_MODAL_ALIAS = "Umbraco.Community.SearchManager.Modal.NodePresence";

/** What the sidebar shows. */
export interface NodePresenceModalData {
  /** The document key. */
  key: string;
}

/** Opens the sidebar that shows which search indexes hold a document. */
export const SEARCH_MANAGER_NODE_PRESENCE_MODAL = new UmbModalToken<NodePresenceModalData, never>(
  SEARCH_MANAGER_NODE_PRESENCE_MODAL_ALIAS,
  { modal: { type: "sidebar", size: "medium" } },
);
