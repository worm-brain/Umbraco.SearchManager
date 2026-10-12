import type { IndexSummary, RebuildRun } from "../api/types.gen.js";

/**
 * Index names compare case-insensitively, as Examine's do; this is the key every map here uses.
 *
 * @param name - An index name.
 * @returns The comparison key.
 */
export function indexKey(name: string): string {
  return name.toLowerCase();
}

/**
 * The newest run per index (the API lists runs oldest first, so later runs win).
 *
 * @param runs - The runs, oldest first.
 * @returns The latest run of each index, keyed by {@link indexKey}.
 */
export function latestRunByIndex(runs: readonly RebuildRun[]): Map<string, RebuildRun> {
  const latest = new Map<string, RebuildRun>();
  for (const run of runs) latest.set(indexKey(run.indexName), run);
  return latest;
}

/**
 * Whether a run is still to finish (queued or running).
 *
 * @param run - The run, if any.
 * @returns True for a queued or running run.
 */
export function isActive(run: RebuildRun | undefined): boolean {
  return run?.status === "Queued" || run?.status === "Running";
}

/**
 * Whether any run is still to finish, so the view keeps polling.
 *
 * @param runs - The runs.
 * @returns True when one is queued or running.
 */
export function hasActiveRuns(runs: readonly RebuildRun[]): boolean {
  return runs.some((run) => isActive(run));
}

/**
 * Why an index cannot be queued for a rebuild right now, or undefined when it can.
 *
 * @param index - The index.
 * @param latest - Its latest run, if any.
 * @param busyReason - The text for an index that is already queued or running.
 * @returns The reason, or undefined.
 */
export function rebuildBlockReason(
  index: IndexSummary,
  latest: RebuildRun | undefined,
  busyReason: string,
): string | undefined {
  if (!index.capabilities.rebuild) return index.rebuildUnavailableReason ?? undefined;
  return isActive(latest) ? busyReason : undefined;
}

/**
 * The estimated percentage of a running rebuild: documents written so far over the count the run
 * is expected to reach (the previous run's final count). Capped at 99 because the run is not done
 * until the server says so.
 *
 * @param run - The run.
 * @returns A whole percentage, or null when there is nothing to estimate from.
 */
export function estimatePercent(run: RebuildRun): number | null {
  if (run.status !== "Running" || run.documentsWritten == null || !run.estimatedTotal) return null;
  return Math.max(0, Math.min(99, Math.round((run.documentsWritten / run.estimatedTotal) * 100)));
}

/**
 * Where a queued one-at-a-time run is in line (1 = next). All-at-once runs start at once, so they
 * have no position.
 *
 * @param runs - Every run, oldest first.
 * @param run - The queued run.
 * @returns Its 1-based position, or null.
 */
export function waitPosition(runs: readonly RebuildRun[], run: RebuildRun): number | null {
  if (run.status !== "Queued" || run.mode !== "OneAtATime") return null;
  const line = runs.filter((candidate) => candidate.status === "Queued" && candidate.mode === "OneAtATime");
  return line.findIndex((candidate) => candidate.id === run.id) + 1;
}

/**
 * Toggles the header "select all": selects every eligible index when not all are selected,
 * otherwise clears them. Selections of indexes outside `eligible` are left alone.
 *
 * @param selected - The selected index names.
 * @param eligible - The names the header covers (visible and rebuildable).
 * @returns The new selection.
 */
export function toggleAll(selected: ReadonlySet<string>, eligible: readonly string[]): Set<string> {
  const next = new Set(selected);
  const all = eligible.length > 0 && eligible.every((name) => next.has(name));
  for (const name of eligible) {
    if (all) next.delete(name);
    else next.add(name);
  }
  return next;
}

/**
 * Formats a duration between two timestamps for the Rebuilds panel.
 *
 * @param from - The start (ISO string).
 * @param to - The end (ISO string), or now when missing.
 * @param now - The current time, for a run still going.
 * @returns For example "45 s" or "2 min 5 s".
 */
export function formatDuration(from: string, to: string | null | undefined, now = new Date()): string {
  const seconds = Math.max(0, Math.round(((to ? Date.parse(to) : now.getTime()) - Date.parse(from)) / 1000));
  if (seconds < 60) return `${seconds} s`;
  const minutes = Math.floor(seconds / 60);
  const rest = seconds % 60;
  return rest === 0 ? `${minutes} min` : `${minutes} min ${rest} s`;
}
