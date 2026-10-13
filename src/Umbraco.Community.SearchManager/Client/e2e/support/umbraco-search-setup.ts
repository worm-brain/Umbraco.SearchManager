/** The parts of a `GET /indexes` entry the setup step reads. */
export interface ApiIndex {
  name: string;
  engine: string;
  documentCount?: number | null;
}

/** The parts of a `GET /rebuilds` run the setup step reads. */
export interface ApiRun {
  indexName: string;
  status: string;
}

/** The engine id of Umbraco Search indexes (the adapter's `UmbracoSearchIndexSource.EngineId`). */
const UMBRACO_SEARCH = "umbraco-search";

/**
 * Picks the Umbraco Search indexes that hold no documents, which the setup step rebuilds.
 *
 * @param indexes - The indexes `GET /indexes` lists.
 * @returns Their names.
 */
export function emptyUmbracoSearchIndexes(indexes: ReadonlyArray<ApiIndex>): Array<string> {
  return indexes.filter((index) => index.engine === UMBRACO_SEARCH && !index.documentCount).map((index) => index.name);
}

/**
 * Whether every rebuild of the given indexes has finished, however it ended. A rebuild of an index
 * with nothing to index (no media, no members) succeeds with nothing in it, which is fine here.
 *
 * @param runs - The runs `GET /rebuilds` lists.
 * @param names - The indexes the setup step rebuilt.
 * @returns True when each of them has a run and none is still queued or running.
 */
export function settled(runs: ReadonlyArray<ApiRun>, names: ReadonlyArray<string>): boolean {
  return names.every((name) => {
    const own = runs.filter((run) => run.indexName === name);
    return own.length > 0 && own.every((run) => run.status !== "Queued" && run.status !== "Running");
  });
}
