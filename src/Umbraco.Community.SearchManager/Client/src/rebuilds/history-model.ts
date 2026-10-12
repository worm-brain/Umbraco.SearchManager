import type { RebuildHistoryEntry } from "../api/types.gen.js";
import { indexKey } from "./rebuild-model.js";

/** A past time as a value and unit for `localize.relativeTime` (the value is negative or zero). */
export interface AgoParts {
  /** How many units ago, as a negative number (zero for "now"). */
  value: number;
  /** The largest unit that fits. */
  unit: Intl.RelativeTimeFormatUnit;
}

/** Units from largest to smallest, with their length in seconds. */
const UNITS: ReadonlyArray<[Intl.RelativeTimeFormatUnit, number]> = [
  ["year", 365 * 24 * 3600],
  ["month", 30 * 24 * 3600],
  ["week", 7 * 24 * 3600],
  ["day", 24 * 3600],
  ["hour", 3600],
  ["minute", 60],
];

/**
 * How long ago a time was, in the largest whole unit, for "Last rebuilt 2 days ago". A time in
 * the future (clock skew between servers) counts as now.
 *
 * @param from - The time (ISO string).
 * @param now - The current time.
 * @returns The value and unit.
 */
export function agoParts(from: string, now = new Date()): AgoParts {
  const seconds = Math.max(0, Math.floor((now.getTime() - Date.parse(from)) / 1000));
  for (const [unit, length] of UNITS) {
    if (seconds >= length) return { value: -Math.floor(seconds / length), unit };
  }
  return { value: -seconds || 0, unit: "second" };
}

/**
 * Keys the latest history entries (one per index, from `GET /rebuilds/history/latest`) by index.
 *
 * @param entries - The entries.
 * @returns The entries keyed by {@link indexKey}.
 */
export function latestEntryByIndex(entries: readonly RebuildHistoryEntry[]): Map<string, RebuildHistoryEntry> {
  return new Map(entries.map((entry) => [indexKey(entry.indexName), entry]));
}
