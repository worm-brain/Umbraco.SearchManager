import type { RebuildRun } from "../api/types.gen.js";
import { hasActiveRuns } from "./rebuild-model.js";

/** Fetches the server's runs, in hey-api's non-throwing result shape. */
export type FetchRunsFn = () => Promise<{ data?: RebuildRun[]; error?: unknown; response?: Response }>;

/** Timer functions, injectable so tests can drive time. */
export interface PollTimers {
  /** Schedules `callback` after `ms` milliseconds. */
  set: (callback: () => void, ms: number) => unknown;
  /** Cancels a scheduled callback. */
  clear: (handle: unknown) => void;
}

const defaultTimers: PollTimers = {
  set: (callback, ms) => setTimeout(callback, ms),
  clear: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

/**
 * Keeps the Rebuilds panel's runs fresh by polling `GET /rebuilds` while any run is queued or
 * running, and stops once they have all finished. Each answer goes to `onRuns`; a failed request
 * keeps the last runs and tries again on the next tick.
 */
export class RebuildsPoller {
  #handle: unknown;
  #stopped = false;

  /**
   * @param fetchRuns - Performs the request.
   * @param onRuns - Receives each list of runs.
   * @param intervalMs - How long to wait between polls while runs are active.
   * @param timers - Timer functions (tests pass fakes).
   */
  constructor(
    private readonly fetchRuns: FetchRunsFn,
    private readonly onRuns: (runs: RebuildRun[]) => void,
    private readonly intervalMs = 2000,
    private readonly timers: PollTimers = defaultTimers,
  ) {}

  /**
   * Fetches now, and keeps polling while runs are active. Call it after queueing, cancelling or
   * whenever the view should catch up.
   *
   * @returns A promise that settles once this fetch is handled.
   */
  async refresh(): Promise<void> {
    this.#clear();
    this.#stopped = false;
    const { data } = await this.fetchRuns().catch(() => ({ data: undefined }));
    if (this.#stopped) return;
    if (data) this.onRuns(data);
    // Keep polling while something is still to finish, or when the request failed (it may be a
    // blip, and the runs on screen could still be moving).
    if (!data || hasActiveRuns(data)) this.#handle = this.timers.set(() => void this.refresh(), this.intervalMs);
  }

  /** Stops polling (call when the view leaves the page). */
  stop(): void {
    this.#stopped = true;
    this.#clear();
  }

  #clear(): void {
    if (this.#handle !== undefined) this.timers.clear(this.#handle);
    this.#handle = undefined;
  }
}
