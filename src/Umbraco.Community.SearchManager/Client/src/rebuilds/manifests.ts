import { SEARCH_MANAGER_REBUILD_CONFIRM_MODAL_ALIAS } from "./rebuild-confirm-modal.token.js";

/** The rebuild confirmation modal, opened by the Indexes view before anything is queued. */
export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "modal",
    alias: SEARCH_MANAGER_REBUILD_CONFIRM_MODAL_ALIAS,
    name: "Search Manager Rebuild Confirmation Modal",
    element: () => import("./rebuild-confirm-modal.element.js"),
  },
];
