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
 * Keeps the Rebuilds panel's runs fresh. Each answer of `GET /rebuilds` goes to `onRuns`.
 *
 * Two modes: while live server events are connected, it fetches only when asked (on each event);
 * otherwise it polls while any run is queued or running, and stops once they have all finished.
 * A failed request keeps the last runs and, when polling, tries again on the next tick. Refreshes
 * asked for while a fetch is in flight fold into one more fetch, so a burst of events costs two
 * requests at most.
 */
export class RebuildsPoller {
  #handle: unknown;
  #stopped = false;
  #polling = true;
  #inFlight?: Promise<void>;
  #again = false;

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
   * Fetches now and, when polling, keeps polling while runs are active. Call it after queueing or
   * cancelling, on a server event, or whenever the view should catch up.
   *
   * @returns A promise that settles once this fetch is handled.
   */
  refresh(): Promise<void> {
    this.#stopped = false;
    if (this.#inFlight) {
      this.#again = true;
      return this.#inFlight;
    }
    this.#inFlight = this.#fetch().finally(() => {
      this.#inFlight = undefined;
      if (this.#again && !this.#stopped) {
        this.#again = false;
        void this.refresh();
      }
    });
    return this.#inFlight;
  }

  /**
   * Turns interval polling on (no live events) or off (live events connected). Turning it on
   * fetches at once, in case an event was missed while the connection was down.
   *
   * @param enabled - Whether to poll.
   * @returns A promise that settles once the catch-up fetch is handled, if there is one.
   */
  async setPolling(enabled: boolean): Promise<void> {
    if (this.#polling === enabled) return;
    this.#polling = enabled;
    this.#clear();
    if (enabled && !this.#stopped) await this.refresh();
  }

  /** Stops polling (call when the view leaves the page). */
  stop(): void {
    this.#stopped = true;
    this.#again = false;
    this.#clear();
  }

  /** Performs one fetch and schedules the next poll when due. */
  async #fetch(): Promise<void> {
    this.#clear();
    const { data } = await this.fetchRuns().catch(() => ({ data: undefined }));
    if (this.#stopped) return;
    if (data) this.onRuns(data);
    // Keep polling while something is still to finish, or when the request failed (it may be a
    // blip, and the runs on screen could still be moving).
    if (this.#polling && (!data || hasActiveRuns(data))) {
      this.#handle = this.timers.set(() => void this.refresh(), this.intervalMs);
    }
  }

  #clear(): void {
    if (this.#handle !== undefined) this.timers.clear(this.#handle);
    this.#handle = undefined;
  }
}
