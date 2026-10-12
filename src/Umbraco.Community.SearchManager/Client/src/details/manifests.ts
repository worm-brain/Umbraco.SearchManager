import { SEARCH_MANAGER_INDEX_DETAILS_MODAL_ALIAS } from "./index-details-modal.token.js";

/** The index details sidebar, opened from a row of the Indexes view. */
export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "modal",
    alias: SEARCH_MANAGER_INDEX_DETAILS_MODAL_ALIAS,
    name: "Search Manager Index Details Modal",
    element: () => import("./index-details-modal.element.js"),
  },
];
