/** The engine filter's value: every engine, or one engine id (`examine`, `umbraco-search`, ...). */
export type EngineFilter = "all" | (string & {});

/** One button of the engine filter. */
export interface EngineOption {
  /** The filter value the button selects. */
  value: EngineFilter;
  /** How many indexes the button would show. */
  count: number;
}

/**
 * Lists the engine filter's buttons: "all" first, then each engine in the order its first index
 * appears (the API lists core Examine before Umbraco Search).
 *
 * @param indexes - The indexes to count, each with its engine id.
 * @returns The options with their counts.
 */
export function engineOptions(indexes: ReadonlyArray<{ engine: string }>): EngineOption[] {
  const counts = new Map<string, number>();
  for (const index of indexes) counts.set(index.engine, (counts.get(index.engine) ?? 0) + 1);
  return [{ value: "all", count: indexes.length }, ...[...counts].map(([value, count]) => ({ value, count }))];
}

/**
 * Keeps the indexes the engine filter shows. An engine that no longer has any index shows nothing
 * rather than silently switching back to "all".
 *
 * @param indexes - The indexes to filter.
 * @param engine - The selected filter.
 * @returns The indexes of that engine, or all of them for "all".
 */
export function filterByEngine<T extends { engine: string }>(indexes: readonly T[], engine: EngineFilter): T[] {
  return engine === "all" ? [...indexes] : indexes.filter((index) => index.engine === engine);
}
