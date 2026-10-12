import { UMB_WORKSPACE_CONDITION_ALIAS } from "@umbraco-cms/backoffice/workspace";
import { SEARCH_MANAGER_WORKSPACE_ALIAS } from "../workspace/constants.js";

/**
 * The Indexes tab of the Search Manager workspace. Tabs sort by weight, highest first: Indexes (300),
 * Explore (200), Diagnose (100); the first tab is the workspace's default.
 */
export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "workspaceView",
    alias: "Umbraco.Community.SearchManager.WorkspaceView.Indexes",
    name: "Search Manager Indexes Workspace View",
    element: () => import("./indexes-view.element.js"),
    weight: 300,
    meta: {
      label: "#searchManager_tabIndexes",
      pathname: "indexes",
      icon: "icon-server",
    },
    conditions: [{ alias: UMB_WORKSPACE_CONDITION_ALIAS, match: SEARCH_MANAGER_WORKSPACE_ALIAS }],
  },
];
