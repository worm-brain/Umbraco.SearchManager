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
  },
} satisfies UmbLocalizationDictionary;
