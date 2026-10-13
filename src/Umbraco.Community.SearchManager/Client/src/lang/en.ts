import type { UmbLocalizationDictionary } from "@umbraco-cms/backoffice/localization-api";

/**
 * English strings for Search Manager.
 *
 * Every key sits in the `searchManager` section, so manifests reference them as
 * `#searchManager_{key}` and elements as `this.localize.term("searchManager_{key}")`. The
 * backoffice falls back to `en` for any culture without its own dictionary. Entries that take
 * values are functions; `localize.term(key, ...args)` passes the arguments through.
 */
/**
 * A count in British English digit grouping, or `-` when unknown.
 *
 * @param count - The count.
 * @returns The text.
 */
function n(count: number | null | undefined): string {
  return count == null ? "-" : count.toLocaleString("en-GB");
}

/**
 * A count with its noun, singular for exactly one: `1 document`, `2,104 documents`.
 *
 * @param count - The count; unknown reads `-` with the plural.
 * @param one - The singular noun.
 * @param many - The plural noun.
 * @returns The text.
 */
function plural(count: number | null | undefined, one: string, many: string): string {
  return `${n(count)} ${count === 1 ? one : many}`;
}

export default {
  searchManager: {
    // Workspace, menu item and tabs.
    title: "Search Manager",
    tabIndexes: "Indexes",
    tabExplore: "Explore",
    tabDiagnose: "Diagnose",

    // Diagnose view: the shell and the Fields tool.
    diagnoseTool: "Tool",
    diagnoseFields: "Fields",
    diagnoseNoTargets: "There is no index to diagnose.",
    fieldStatsHeadline: (index: string) => `Fields in ${index}`,
    fieldStatsLoading: "Reading the index. On a large index this takes a few seconds.",
    fieldStatsError: (message: string) => `Could not read the field statistics: ${message}`,
    fieldStatsUnavailable: "Field statistics are not available for this index.",
    fieldStatsDocuments: (count: string) => `${count} documents`,
    fieldStatsDeleted: (count: string) => `${count} deleted`,
    fieldStatsPerCulture:
      "Umbraco Search keeps one document per culture, so the counts are culture documents, not content items",
    fieldStatsEstimate: "Coverage figures are estimates",
    fieldStatsNoMissingLink: (reason: string) => `Missing counts do not link to Explore: ${reason}`,
    fieldStatsShowAll: "Show system fields, culture variants and raw copies",
    fieldStatsNoFields: "No fields match.",
    fieldStatsTableLabel: "Field statistics",
    fieldStatsColumnField: "Field",
    fieldStatsColumnType: "Type",
    fieldStatsColumnAnalyzer: "Analyzer",
    fieldStatsColumnCoverage: "Coverage",
    fieldStatsColumnUnique: "Unique terms",
    fieldStatsColumnTopTerms: "Top terms (click to search)",
    fieldStatsColumnMissing: "Missing",
    fieldStatsGroupSystem: "System field",
    fieldStatsGroupCulture: (culture: string) => `Culture variant (${culture})`,
    fieldStatsGroupRaw: "Raw copy, not analysed",
    fieldStatsGroupSegment: "Segment",
    fieldStatsStoredOnly: "Stored only, not searchable",
    fieldStatsAtLeast: (count: string) => `at least ${count}`,
    fieldStatsAnalyzed: "Analyzed terms, not whole values",
    fieldStatsRange: (min: string, max: string) => `${min} to ${max}`,
    fieldStatsMissing: (count: string) => `${count} missing`,
    fieldStatsMissingLink: (count: string, field: string) => `Search the ${count} documents missing ${field}`,

    // Diagnose view: the Find a node tool and the "Show in search indexes" action.
    diagnoseFindNode: "Find a node",
    showInSearchIndexes: "Show in search indexes",
    openInSearchManager: "Open in Search Manager",
    nodeSearchTitle: "Find a document",
    nodeSearchLabel: "Document name, id or key",
    nodeSearchPlaceholder: "Type a document name, id or key",
    nodeSearchError: (message: string) => `Could not look the document up: ${message}`,
    nodeNoCandidates: "No document matches.",
    nodeCandidateTrashed: (id: string) => `${id} · in the recycle bin`,
    nodeNotFound: "No document has this key.",
    nodeLoadError: (message: string) => `Could not load where the document is indexed: ${message}`,
    nodeKey: "Key",
    nodePublished: "Published",
    nodeUnpublished: "Not published",
    nodeTrashed: "In the recycle bin",
    nodeProtected: "Protected",
    nodeCulturePublished: "published",
    nodeCultureDraft: "published, with unpublished changes",
    nodeCultureUnpublished: "not published",
    nodePresenceTitle: "Search indexes",
    nodePresenceLabel: "Where the document is indexed",
    nodeNoIndexes: "There is no index to check.",
    nodeColumnStatus: "Status",
    nodeColumnReason: "Why",
    nodePresent: "Present",
    nodeMissing: "Missing",
    nodeNotApplicable: "Not applicable",
    nodeStale: "Stale",
    nodeStaleTitle: (indexed: string, expected: string) =>
      `The index holds the version from ${indexed}; the document was updated ${expected}.`,
    nodeViewFields: (index: string) => `View the document's fields in ${index}`,
    nodeViewFieldsShort: "View fields",
    matchTitle: "Why doesn't this query match it?",
    matchNoIndex: "No index holds this document, so there is nothing to test a query against.",
    matchQueryLabel: "Query to test",
    matchQueryPlaceholder: "Words or field:value, as in Explore (Enter tests it)",
    matchRun: "Test",
    matchError: (message: string) => `Could not test the query: ${message}`,
    matchYes: "Matches this document",
    matchNo: "Does not match this document",
    matchNotInIndex: "This document is not in the index",
    clauseMatched: "matched",
    clauseNotMatched: "not matched",
    clauseSkipped: "skipped",
    matchNodeValues: (field: string) => `The document's ${field}:`,
    matchNoValue: "(no value)",
    matchOrNote:
      "Filters on the same field are ORed together, so the overall result can differ from the filters one by one.",

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
    healthIncompleteDetail: (documents: string, error: string) => {
      const failed = documents
        ? `Last rebuild failed after writing ${documents} documents`
        : "Last rebuild failed part-way";
      return error ? `${failed}: ${error}` : failed;
    },
    healthUnknown: "Unknown",

    // Explore view: pickers and search box.
    exploreIndex: "Index",
    exploreCulture: "Culture",
    exploreAllCultures: "All cultures",
    exploreNoCulture: "No culture (invariant content only)",
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
    chipDisabled: (reason: string) => `${reason}. Left out of the search.`,
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
    resultsCount: (count: number) => plural(count, "result", "results"),
    resultsWhole: (count: number) => `${plural(count, "document", "documents")}, newest first`,
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

    // Explain a score: the drawer's "Explain score" and the score tree.
    explainButton: "Explain score",
    explainNotLucene: (target: string) => `${target} is not a Lucene index, so its scores cannot be explained`,
    explainUmbracoSearch: "Umbraco Search does not return scores",
    explainNotRanked: "Not ranked: only filters, sorted newest first",
    explainLoading: "Explaining the score",
    explainError: (message: string) => `Could not explain the score: ${message}`,
    explainNotFound: "This document is no longer in the index",
    explainNotMatched: "This document no longer matches the search (the index has changed since)",
    explainTreeLabel: (name: string) => `How the score of ${name} is made up`,
    explainExcludedLabel: "Filters that add no score",
    explainTotal: (score: string) => `Score ${score}`,
    explainSum: "The sum of",
    explainCoord: (matched: string, of: string) =>
      `${matched} of ${of} optional ${of === "1" ? "clause" : "clauses"} matched, so the score is scaled by ${matched}/${of}`,
    explainTermMatch: (field: string, word: string, times: string) => `${field} contains "${word}" ${times}`,
    explainPhraseMatch: (field: string, phrase: string, times: string) =>
      `${field} contains the phrase "${phrase}" ${times}`,
    explainOnce: "once",
    explainTimes: (count: string) => `${count} times`,
    explainPrefix: (field: string, prefix: string) => `${field} starts with "${prefix}" (every match scores the same)`,
    explainWildcard: (field: string, pattern: string) => `${field} matches "${pattern}" (every match scores the same)`,
    explainMatchAll: "Matches every document: every result scores the same",
    explainFilter: (chip: string) => `must match: ${chip}`,
    explainMustNotMatch: (chip: string) => `must not match ${chip}, adds no score`,
    explainNoScore: (chip: string) => `must match: ${chip}, adds no score`,
    explainTf: (value: string) => `tf ${value}`,
    explainIdf: (value: string) => `idf ${value}`,
    explainFieldNorm: (value: string) => `field norm ${value}`,
    explainBoost: (value: string) => `boost ${value}`,
    explainDocFreq: (docFreq: string, maxDocs: string) =>
      `in ${docFreq} of ${maxDocs} ${maxDocs === "1" ? "document" : "documents"}`,
    explainMore: (count: number) => (count === 1 ? "1 clause" : `${count} clauses`),
    explainFilterDetail: "How the clause matched",

    // Compare two scores.
    compareColumn: "Compare",
    compareSelect: (name: string) => `Compare the score of ${name}`,
    compareLimit: "Two results are already picked",
    compareButton: "Compare scores",
    compareTitle: "Compare scores",
    compareClose: "Close comparison",
    compareTableLabel: "Score comparison",
    compareDifferencesOnly: "Differences only",
    compareWhat: "What",
    compareDifference: "Difference (A - B)",
    compareDocument: (side: string, name: string, score: string) => `${side}: ${name}, score ${score}`,
    compareNone: "Both scores are made up the same way.",
    compareSideA: "A",
    compareSideB: "B",
    compareProblem: (side: string, name: string, reason: string) => `${side} (${name}): ${reason}`,
    compareQueryNorm: "Query normalisation (multiplies every word; the same for both)",
    compareCoord: "Scaled down for optional clauses that did not match",
    compareTerm: (field: string, word: string) => `${field} contains "${word}"`,
    comparePhrase: (field: string, phrase: string) => `${field} contains the phrase "${phrase}"`,
    compareConstant: (field: string, pattern: string) => `${field} matches "${pattern}"`,

    // Suggestions and the fields panel.
    suggestFields: "Fields",
    suggestValues: (field: string) => `Values of ${field}`,
    suggestTerm: "term",
    suggestSampled: "The field has too many terms to read them all, so some values are left out.",
    suggestText: "Search all fields for this text",
    suggestHint: "Up and down to move, Enter to pick, Escape to close, -field:value excludes",
    fieldsPanel: "Fields",
    fieldsWholeIndex: (count: number) => `whole index, ${plural(count, "document", "documents")}`,
    fieldsResults: (count: number) => (count === 1 ? "this 1 result" : `these ${n(count)} results`),
    fieldsSampled: "Some values are left out: the field has more values than are counted.",
    fieldsError: (message: string) => `Could not count values: ${message}`,
    fieldsNoValues: "No values",
    fieldsAll: (count: string) => `All fields (${count})`,
    fieldsFilter: "Filter fields",
    fieldsInclude: (field: string, value: string) => `Filter on ${field} ${value}`,
    fieldsExclude: (field: string, value: string) => `Exclude ${field} ${value}`,
    fieldsAdd: (field: string) => `Add a filter on ${field}`,
    fieldsCoverage: (coverage: string) => `${coverage} of documents`,
    relativeToday: "today",
    relativeDays: (days: string) => `last ${days} days`,
    editorNewTitle: (field: string) => `New filter: ${field}`,

    // Show query and native mode.
    showQuery: "Show query",
    hideQuery: "Hide query",
    nativeOn: "Lucene",
    nativeOnLabel: "Edit the query as raw Lucene",
    nativeOff: "Back to filters",
    nativeUnavailable: (index: string) => `${index} is not searched by Lucene, so it takes no raw Lucene queries.`,
    nativeLabel: "Lucene query",
    nativePlaceholder: "A Lucene query, for example +__NodeTypeAlias:blogpost +level:[2 TO 3]",
    nativeRun: "Run",
    nativeCannotParse: "Cannot parse this query",
    nativeInvalid: (message: string) => message,
    nativeInvalidAt: (message: string, position: string) => `${message} (at character ${position})`,
    nativeCheckFailed: (message: string) => `Could not check the query: ${message}`,
    nativeFix: (replacement: string) => `Fix: ${replacement}`,
    nativeReadError: (message: string) => `Could not turn the query into filters: ${message}`,
    queryPanel: "Generated query",
    queryLucene: (language: string) => `${language} query sent`,
    queryParsed: "As Lucene parsed it (bare words search every field)",
    queryNotParsed: "(does not parse)",
    queryCultureNote: (culture: string) => `The culture picks the _${culture} variant of fields that have one.`,
    queryCSharp: "C# with Examine",
    queryCSharpNative: "C# with Examine (NativeQuery: not every filter has a fluent form)",
    queryRequest: (index: string) => `Umbraco Search request for ${index} (C#)`,
    queryRequestNote:
      "Document types are shown by alias; the filter sends the content type key. Without a culture, Umbraco Search finds only documents that do not vary by culture.",
    queryCopy: "Copy",
    queryNotSent: (chip: string, reason: string) => `Left out: ${chip}. ${reason}.`,
    queryError: (message: string) => `Could not show the query: ${message}`,
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
    rebuildsRunning: (written: number | null, total: number | null, percent: number) =>
      `About ${n(written)} of ${plural(total, "document", "documents")} (${percent}%), estimated from the last run`,
    rebuildsRunningNoEstimate: (written: number | null) => `${plural(written, "document", "documents")} so far`,
    rebuildsRunningZeroDowntime: (percent: number | null) =>
      percent === null
        ? "Building a fresh copy. Search keeps using the current one."
        : `Building a fresh copy. Search keeps using the current one. About ${percent}%, estimated`,
    rebuildsSucceeded: (duration: string, documents: number | null) =>
      `Done in ${duration}, ${plural(documents, "document", "documents")}`,
    rebuildsSucceededEmpty: (duration: string) => `Done in ${duration}, nothing to index`,
    rebuildsFailed: (duration: string, error: string) => `Failed after ${duration}: ${error}`,
    rebuildsCancelled: "Cancelled before it started",

    // Rebuild history: the "Last rebuilt" column and the details sidebar.
    columnLastRebuilt: "Last rebuilt",
    lastRebuiltNone: "Not recorded",
    lastRebuiltSucceeded: (duration: string) => `${duration}, succeeded`,
    lastRebuiltFailed: (duration: string) => `Failed after ${duration}`,
    detailsOpen: (name: string) => `Details and history for ${name}`,
    detailsHistoryTitle: "Rebuild history",
    detailsHistoryHint: "Kept in the database, last 20 runs",
    detailsNoRuns: "No rebuilds recorded yet.",
    detailsLoadError: (message: string) => `Could not load the history: ${message}`,
    detailsPropertiesTitle: "Index properties",
    detailsNoProperties: "This index reports no properties.",
    historyTriggerUser: (name: string) => `${name}, from Search Manager`,
    historyTriggerOther: "Started outside Search Manager",
    historyTriggerStartup: "At startup",
    historySucceeded: "Succeeded",
    historyFailed: "Failed",
    historyDetail: (duration: string, before: string, after: string, server: string) =>
      `${duration}, documents ${before} to ${after}, on ${server}`,
    // The Search Manager box on Umbraco Search's index page.
    boxRecentRuns: "Earlier runs",
    boxExplore: "Explore",
    boxFieldStatistics: "Field statistics",
    boxHistory: "History",

    // Server: which server the user is on, and the load-balancing warning.
    serverLine: (machine: string, role: string) => `Server ${machine} (${role})`,
    serverRoleSingle: "single server",
    serverRoleSchedulingPublisher: "scheduling publisher",
    serverRoleSubscriber: "subscriber",
    serverRoleUnknown: "role unknown",
    loadBalancedBanner: (machine: string, count: number) =>
      `This site runs on ${count} servers. Rebuilding a core Examine index only rebuilds it on this server (${machine}); the other servers keep their own copy. Umbraco Search indexes rebuild on every server.`,
    confirmThisServerOnly: (machine: string) =>
      `This site runs on more than one server. Core Examine indexes are rebuilt on this server (${machine}) only; Umbraco Search indexes rebuild on every server.`,
    historyDetailNoCounts: (duration: string, server: string) => `${duration}, on ${server}`,
  },
} satisfies UmbLocalizationDictionary;
