import type { UmbLocalizationDictionary } from "@umbraco-cms/backoffice/localization-api";

/**
 * English strings for Search Manager.
 *
 * Every key sits in the `searchManager` section, so manifests reference them as
 * `#searchManager_{key}` and elements as `this.localize.term("searchManager_{key}")`. The
 * backoffice falls back to `en` for any culture without its own dictionary. Entries that take
 * values are functions; `localize.term(key, ...args)` passes the arguments through.
 */
export default {
  searchManager: {
    // Workspace, menu item and tabs.
    title: "Search Manager",
    tabIndexes: "Indexes",
    tabExplore: "Explore",
    tabDiagnose: "Diagnose",
    exploreSoon: "Search an index with filter chips, field suggestions and raw Lucene. Coming in a later slice.",
    diagnoseSoon: "Field statistics, analyzers and where a page is indexed. Coming in a later slice.",

    // Indexes view.
    reload: "Reload",
    indexesCount: (count: number) => (count === 1 ? "1 index" : `${count} indexes`),
    indexesError: (message: string) => `Could not load the indexes: ${message}`,
    indexesTableLabel: "Search indexes",
    columnIndex: "Index",
    columnHealth: "Health",
    columnDocuments: "Documents",
    columnDeleted: "Deleted",
    columnFields: "Fields",
    columnSize: "Size",
    zeroDowntime: "Zero downtime",

    // Health labels, keyed by the API's IndexHealth values.
    healthHealthy: "Healthy",
    healthRebuilding: "Rebuilding",
    healthUnhealthy: "Unhealthy",
    healthCorrupted: "Corrupted",
    healthEmpty: "Empty",
    healthIncomplete: "Incomplete",
    healthUnknown: "Unknown",

    // Engine filter (Umbraco Search adapter).
    engineFilterLabel: "Engine",
    engineAll: (count: number) => `All engines (${count})`,
    engineOption: (engine: string, count: number) => `${engine} (${count})`,

    // Rebuilds: selection, actions and the confirmation.
    columnSelect: "Select",
    columnActions: "Actions",
    selectAll: "Select all rebuildable indexes",
    selectIndex: (name: string) => `Select ${name}`,
    selectedCount: (count: number) => `${count} selected`,
    clearSelection: "Clear selection",
    rebuildSelected: (count: number) => (count === 1 ? "Rebuild 1 index" : `Rebuild ${count} indexes`),
    rebuildAll: "Rebuild all",
    rebuildIndex: (name: string) => `Rebuild ${name}`,
    rebuildUnavailable: (reason: string) => `Rebuild unavailable: ${reason}`,
    rebuildBusy: "Already queued or running",
    rebuildQueuedHealth: "Queued for rebuild",
    rebuildRunningHealth: (percent: number) => `About ${percent}% (estimate)`,
    rebuildRefused: (name: string, reason: string) => `${name} was not queued: ${reason}`,
    rebuildError: (message: string) => `Could not queue the rebuild: ${message}`,
    confirmTitleOne: (name: string) => `Rebuild ${name}?`,
    confirmTitleMany: (count: number) => `Rebuild ${count} indexes?`,
    confirmIntro: "Rebuilds run on the server in the background. You can keep working or leave this page.",
    confirmClearedFirst: "Cleared first: searches on this index return partial results until it finishes.",
    confirmZeroDowntime: "No downtime: search keeps using the current copy until the new one is ready, then swaps.",
    confirmOneAtATime: "Rebuild one at a time (recommended: less load on the site)",
    confirmButtonOne: "Rebuild index",
    confirmButtonMany: (count: number) => `Rebuild ${count} indexes`,

    // Rebuilds panel.
    rebuildsTitle: "Rebuilds",
    rebuildsOneAtATime: "One at a time. They keep running if you leave this page.",
    rebuildsAllAtOnce: "All at once. They keep running if you leave this page.",
    rebuildsClearFinished: "Clear finished",
    rebuildsCancel: "Cancel",
    rebuildsProgressLabel: (name: string) => `${name} rebuild progress, estimated`,
    rebuildsWaiting: (position: number) => (position === 1 ? "Waiting, next in line" : `Waiting, ${position} in line`),
    rebuildsStarting: "Starting",
    rebuildsRunning: (written: string, total: string, percent: number) =>
      `About ${written} of ${total} documents (${percent}%), estimated from the last run`,
    rebuildsRunningNoEstimate: (written: string) => `${written} documents so far`,
    rebuildsRunningZeroDowntime: (percent: number | null) =>
      percent === null
        ? "Building a fresh copy. Search keeps using the current one."
        : `Building a fresh copy. Search keeps using the current one. About ${percent}%, estimated`,
    rebuildsSucceeded: (duration: string, documents: string) => `Done in ${duration}, ${documents} documents`,
    rebuildsFailed: (duration: string, error: string) => `Failed after ${duration}: ${error}`,
    rebuildsCancelled: "Cancelled before it started",
  },
} satisfies UmbLocalizationDictionary;
