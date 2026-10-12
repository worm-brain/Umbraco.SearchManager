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

    // Explore view: pickers and search box.
    exploreIndex: "Index",
    exploreCulture: "Culture",
    exploreAllCultures: "All cultures",
    exploreSearcher: (name: string) => `${name} (searcher)`,
    exploreTargetsError: (message: string) => `Could not load the indexes: ${message}`,
    exploreNoTargets: "There is no index you can search.",
    exploreSchemaError: (message: string) => `Could not read the fields of this index: ${message}`,
    searchLabel: "Search, or add a filter as field:value",
    searchPlaceholder: "Search, or type a filter such as type:blogPost or level:>2 (Enter adds it)",
    searchPlaceholderMore: "Add words or field:value",
    searchUnbalancedQuote: "Unbalanced quote, so this was searched as plain text",
    searchParseError: (message: string) => `Could not read the search: ${message}`,
    searchError: (message: string) => `The search failed: ${message}`,

    // Chips.
    chipDescription: (field: string, op: string, value: string) => `${field} ${op} ${value}`.trim(),
    chipNot: (text: string) => `not ${text}`,
    chipMissing: (field: string) => `${field} is missing`,
    chipHasValue: (field: string) => `${field} has a value`,
    chipEdit: (description: string) => `Edit filter: ${description}`,
    chipRemove: (description: string) => `Remove filter: ${description}`,
    chipDisabled: (reason: string) => `${reason}. Not sent.`,
    chipUnknownField: (field: string, index: string) => `There is no field "${field}" in ${index}`,
    chipWrongOperator: (op: string, type: string) => `"${op}" does not apply to ${type} fields`,
    chipNoLucene: (index: string) => `${index} does not take raw Lucene queries`,
    fieldText: "Text",
    fieldLucene: "Lucene",
    opContains: "contains",
    opPhrase: "has the phrase",
    opStartsWith: "starts with",
    opFuzzy: "is like",
    opIs: "is",
    opEquals: "is",
    opGreaterThan: "is more than",
    opAtLeast: "is at least",
    opLessThan: "is less than",
    opAtMost: "is at most",
    opOn: "is on",
    opAfter: "is on or after",
    opBefore: "is before",
    opMissing: "is missing",

    // Chip editor.
    editorTitle: (field: string) => `Edit filter: ${field}`,
    editorOperator: "Operator",
    editorValue: "Value",
    editorDateHint: "yyyy-mm-dd, today or -7d",
    editorNumberHint: "A number",
    editorExclude: "Exclude matches",
    editorApply: "Apply",
    editorAdd: "Add filter",
    editorCancel: "Cancel",

    // Results.
    resultsLabel: "Search results",
    resultsCount: (count: number) => (count === 1 ? "1 result" : `${count.toLocaleString("en-GB")} results`),
    resultsWhole: (count: number) => `${count.toLocaleString("en-GB")} documents, newest first`,
    resultsNone: "No documents match. Remove a filter, or check the field names.",
    resultsSkipped: (count: number) => (count === 1 ? "1 filter was not sent" : `${count} filters were not sent`),
    columnScore: "Score",
    columnId: "Id",
    columnName: "Name",
    columnDocumentType: "Document type",
    columnCultures: "Cultures",
    columnUpdated: "Updated",
    invariant: "invariant",
    columns: "Columns",
    columnsHint: "Any field in the index, not only this page",
    columnsFilter: "Filter fields",
    openInEditor: (name: string) => `Open ${name} in the editor`,
    showDocument: (name: string) => `Show the fields of ${name}`,

    // Document drawer.
    documentLabel: "Document",
    documentClose: "Close document",
    documentCopy: "Copy as JSON",
    documentCopied: "Copied",
    documentOpen: "Open in editor",
    documentFilterFor: (field: string, value: string) => `Filter for ${field} ${value}`,
    documentFilterOut: (field: string, value: string) => `Filter out ${field} ${value}`,
    groupSystem: "System",
    groupProperties: "Properties",
    groupCultures: "Culture variants",
    groupRaw: "Raw copies, stored but not analysed",
    documentGone: "This document is not in the current results.",
  },
} satisfies UmbLocalizationDictionary;
