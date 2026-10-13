/**
 * What a line of a sample site's console log says about its first boot, for `seed-sites.ts`.
 *
 * - `seeded`: the sample content seeder finished (SampleContentSeeder logs this once).
 * - `hung`: Umbraco reports its cache instruction sync stalled on a hung database connection. On
 *   SQLite with Umbraco Search that is a deadlock that never clears (Umbraco issue 23940: the sync
 *   holds a transaction while it waits on async cache refresher handlers), so the boot is retried
 *   on a fresh database.
 */
export type SeedSignal = "seeded" | "hung";

/** The seeder's completion message. */
export const SEEDED_MESSAGE = "Seeded sample content for Search Manager";

/** Umbraco's stalled-sync message (Umbraco 17.6+). */
export const HUNG_MESSAGE = "may be stalled on a hung database connection";

/**
 * Reads one console line from a booting sample site.
 *
 * @param line - A line of the site's standard output.
 * @returns The signal the line carries, or null for any other line.
 */
export function readSeedSignal(line: string): SeedSignal | null {
  if (line.includes(SEEDED_MESSAGE)) return "seeded";
  if (line.includes(HUNG_MESSAGE)) return "hung";
  return null;
}
