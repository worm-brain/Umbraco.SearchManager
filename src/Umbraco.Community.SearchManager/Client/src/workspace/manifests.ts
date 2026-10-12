import { UMB_ADVANCED_SETTINGS_MENU_ALIAS } from "@umbraco-cms/backoffice/settings";
import { SEARCH_MANAGER_ENTITY_TYPE, SEARCH_MANAGER_WORKSPACE_ALIAS } from "./constants.js";

/**
 * Weight of the Search Manager menu item. The Settings advanced menu sorts highest first; the core
 * items are Relations (800), Log Viewer (300), Extension Insights (200) and Webhooks (100), and Log
 * Explorer uses 250, so 240 places Search Manager just below the log tools.
 */
const MENU_ITEM_WEIGHT = 240;

/**
 * Manifests for the Settings menu item and the workspace it opens.
 *
 * - `menuItem` in `Umb.Menu.AdvancedSettings` (Settings > Advanced). With no `kind` it is the
 *   default menu item, which links to the workspace registered for its `entityType`.
 * - `workspace` of the `default` kind, which supplies `UmbDefaultWorkspaceContext` so the views'
 *   workspace condition can match the alias; the `element` draws the editor with its views.
 */
export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "menuItem",
    alias: "Umbraco.Community.SearchManager.MenuItem",
    name: "Search Manager Menu Item",
    weight: MENU_ITEM_WEIGHT,
    meta: {
      label: "#searchManager_title",
      icon: "icon-search",
      entityType: SEARCH_MANAGER_ENTITY_TYPE,
      menus: [UMB_ADVANCED_SETTINGS_MENU_ALIAS],
    },
  },
  {
    type: "workspace",
    kind: "default",
    alias: SEARCH_MANAGER_WORKSPACE_ALIAS,
    name: "Search Manager Workspace",
    element: () => import("./workspace.element.js"),
    meta: {
      entityType: SEARCH_MANAGER_ENTITY_TYPE,
      // The default workspace kind localises `#` keys for the browser title.
      headline: "#searchManager_title",
    },
  },
];
