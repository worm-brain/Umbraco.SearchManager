import { UMB_WORKSPACE_CONDITION_ALIAS } from "@umbraco-cms/backoffice/workspace";
import { SEARCH_MANAGER_WORKSPACE_ALIAS } from "../workspace/constants.js";

/**
 * The Explore tab of the Search Manager workspace. Tabs sort by weight, highest first: Indexes (300),
 * Explore (200), Diagnose (100); the first tab is the workspace's default.
 */
export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "workspaceView",
    alias: "Umbraco.Community.SearchManager.WorkspaceView.Explore",
    name: "Search Manager Explore Workspace View",
    element: () => import("./explore-view.element.js"),
    weight: 200,
    meta: {
      label: "#searchManager_tabExplore",
      pathname: "explore",
      icon: "icon-search",
    },
    conditions: [{ alias: UMB_WORKSPACE_CONDITION_ALIAS, match: SEARCH_MANAGER_WORKSPACE_ALIAS }],
  },
];
