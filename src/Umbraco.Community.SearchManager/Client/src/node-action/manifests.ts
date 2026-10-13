import { UMB_DOCUMENT_ENTITY_TYPE } from "@umbraco-cms/backoffice/document";
import { UMB_SECTION_USER_PERMISSION_CONDITION_ALIAS } from "@umbraco-cms/backoffice/section";
import { UMB_SETTINGS_SECTION_ALIAS } from "@umbraco-cms/backoffice/settings";
import { SEARCH_MANAGER_NODE_PRESENCE_MODAL_ALIAS } from "./node-presence-modal.token.js";

/**
 * "Show in search indexes" on documents, and the sidebar it opens. The action shows only to
 * users with the Settings section, since the Search Manager API requires it.
 */
export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "entityAction",
    kind: "default",
    alias: "Umbraco.Community.SearchManager.EntityAction.ShowInSearchIndexes",
    name: "Search Manager Show In Search Indexes Entity Action",
    forEntityTypes: [UMB_DOCUMENT_ENTITY_TYPE],
    api: () => import("./show-in-search-indexes.action.js"),
    meta: {
      icon: "icon-search",
      label: "#searchManager_showInSearchIndexes",
    },
    conditions: [{ alias: UMB_SECTION_USER_PERMISSION_CONDITION_ALIAS, match: UMB_SETTINGS_SECTION_ALIAS }],
  },
  {
    type: "modal",
    alias: SEARCH_MANAGER_NODE_PRESENCE_MODAL_ALIAS,
    name: "Search Manager Node Presence Modal",
    element: () => import("./node-presence-modal.element.js"),
  },
];
