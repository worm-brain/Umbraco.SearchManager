import type { Filter, NativeValidation, NativeWarning } from "../api/index.js";
import { describeError } from "../shared/describe-error.js";

/**
 * Applies a warning's one-click fix: its replacement in place of the text it is about.
 *
 * @param text - The raw query.
 * @param warning - A warning with a replacement.
 * @returns The fixed query; the same text when the warning has no fix or no longer fits.
 */
export function applyFix(text: string, warning: NativeWarning): string {
  if (warning.replacement == null || warning.start < 0 || warning.start + warning.length > text.length) return text;
  return `${text.slice(0, warning.start)}${warning.replacement}${text.slice(warning.start + warning.length)}`;
}

/**
 * Splits the query around an error position, for marking the offending character (an error at
 * the end marks a space after the text).
 *
 * @param text - The raw query.
 * @param position - The zero-based error position.
 * @returns The text before, the marked character, and the text after.
 */
export function markPosition(text: string, position: number): { before: string; at: string; after: string } {
  const at = Math.max(0, Math.min(position, text.length));
  return { before: text.slice(0, at), at: text.slice(at, at + 1) || " ", after: text.slice(at + 1) };
}

/**
 * The text "Lucene" starts from when native mode opens: the query the last search ran, unless
 * that was the match-everything query.
 *
 * @param lastQuery - `ExploreResult.query`, or undefined before the first search.
 * @returns The text for the box.
 */
export function nativeStart(lastQuery: string | undefined): string {
  return !lastQuery || lastQuery === "*:*" ? "" : lastQuery;
}

/**
 * The chips after leaving native mode: the ones read back from the raw query, then any chip
 * that could not run before (it was never part of the query, so it is kept as it was).
 *
 * @param read - The chips `POST /read-native` returned.
 * @param keptDisabled - The chips that were disabled when native mode opened.
 * @returns The chip list.
 */
export function chipsAfterNative(read: ReadonlyArray<Filter>, keptDisabled: ReadonlyArray<Filter>): Array<Filter> {
  return [...read, ...keptDisabled];
}

/**
 * The `POST /validate` call the validator depends on, in hey-api's non-throwing result shape.
 * It may reject when `signal` aborts.
 */
export type ValidateFn = (
  query: string,
  signal: AbortSignal,
) => Promise<{ data?: NativeValidation; error?: unknown; response?: Response }>;

/**
 * What the native box shows for the text being typed: `idle` (blank), `pending`, the server's
 * `result`, or `error` when the check itself failed.
 */
export type NativeCheck =
  | { status: "idle" | "pending" }
  | { status: "result"; result: NativeValidation }
  | { status: "error"; message: string };

/** Wait after the last keystroke before checking. */
export const VALIDATE_DEBOUNCE_MS = 400;

/**
 * Checks raw Lucene while the user types: each change restarts the wait, then `POST /validate`
 * runs; a newer change aborts a check in flight and a superseded answer is dropped.
 */
export class NativeValidator {
  #controller?: AbortController;
  #timer?: ReturnType<typeof setTimeout>;

  /**
   * @param validate - Performs the request.
   * @param onChange - Receives every new verdict.
   * @param debounceMs - Wait before sending.
   */
  constructor(
    private readonly validate: ValidateFn,
    private readonly onChange: (check: NativeCheck) => void,
    private readonly debounceMs = VALIDATE_DEBOUNCE_MS,
  ) {}

  /**
   * Schedules a check; blank text reports `idle` at once.
   *
   * @param query - The text in the box.
   * @param immediately - Skip the wait (Enter, or opening native mode).
   * @returns A promise that settles when this check has reported (or was superseded).
   */
  check(query: string, immediately = false): Promise<void> {
    this.cancel();
    if (query.trim().length === 0) {
      this.onChange({ status: "idle" });
      return Promise.resolve();
    }

    this.onChange({ status: "pending" });
    return new Promise((resolve) => {
      this.#timer = setTimeout(() => void this.#run(query).then(resolve), immediately ? 0 : this.debounceMs);
    });
  }

  /** Drops any pending or running check without reporting. */
  cancel(): void {
    clearTimeout(this.#timer);
    this.#timer = undefined;
    this.#controller?.abort();
    this.#controller = undefined;
  }

  async #run(query: string): Promise<void> {
    const controller = new AbortController();
    this.#controller = controller;
    try {
      const { data, error, response } = await this.validate(query, controller.signal);
      if (controller !== this.#controller) return;
      this.#controller = undefined;
      this.onChange(
        data ? { status: "result", result: data } : { status: "error", message: describeError(error, response) },
      );
    } catch (error) {
      if (controller !== this.#controller) return;
      this.#controller = undefined;
      this.onChange({ status: "error", message: error instanceof Error ? error.message : String(error) });
    }
  }
}
