import type { DiagnoseViewState } from "./diagnose-state.js";

/**
 * Fired by a Diagnose tool when part of the view's state it owns changes (the field filter, the
 * variants checkbox, ...). Bubbles and is composed so the Diagnose view receives it across
 * shadow roots, merges it into its state and writes the URL.
 */
export class SearchManagerDiagnoseChangeEvent extends Event {
  /** The event type. */
  static readonly TYPE = "search-manager-diagnose-change";

  /**
   * @param change - The keys that changed, with their new values.
   */
  constructor(readonly change: Partial<DiagnoseViewState>) {
    super(SearchManagerDiagnoseChangeEvent.TYPE, { bubbles: true, composed: true });
  }
}

declare global {
  interface HTMLElementEventMap {
    [SearchManagerDiagnoseChangeEvent.TYPE]: SearchManagerDiagnoseChangeEvent;
  }
}
