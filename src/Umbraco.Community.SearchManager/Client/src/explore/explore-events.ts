import type { Filter } from "../api/index.js";

/**
 * Fired by the search box when the chip list changes (parsed input, an edit, a removal). Bubbles
 * and is composed so the Explore view receives it across shadow roots.
 */
export class SearchManagerFiltersChangeEvent extends Event {
  /** The event type. */
  static readonly TYPE = "search-manager-filters-change";

  /**
   * @param filters - The new chip list.
   */
  constructor(readonly filters: Array<Filter>) {
    super(SearchManagerFiltersChangeEvent.TYPE, { bubbles: true, composed: true });
  }
}

/**
 * Fired by the document drawer and the fields panel to add one chip. `toggle` removes the chip
 * instead when it is already there (a value clicked twice in the fields panel).
 */
export class SearchManagerAddFilterEvent extends Event {
  /** The event type. */
  static readonly TYPE = "search-manager-add-filter";

  /**
   * @param filter - The chip.
   * @param toggle - Whether a second add of the same chip removes it.
   */
  constructor(
    readonly filter: Filter,
    readonly toggle = false,
  ) {
    super(SearchManagerAddFilterEvent.TYPE, { bubbles: true, composed: true });
  }
}

/** Fired by a chip's editor with the edited chip. Bubbles to the chip that hosts it. */
export class SearchManagerChipChangeEvent extends Event {
  /** The event type. */
  static readonly TYPE = "search-manager-chip-change";

  /**
   * @param filter - The edited chip.
   */
  constructor(readonly filter: Filter) {
    super(SearchManagerChipChangeEvent.TYPE, { bubbles: true, composed: true });
  }
}

/** Fired by a chip's remove button. */
export class SearchManagerChipRemoveEvent extends Event {
  /** The event type. */
  static readonly TYPE = "search-manager-chip-remove";

  constructor() {
    super(SearchManagerChipRemoveEvent.TYPE, { bubbles: true, composed: true });
  }
}

/** Fired by the chip editor's Cancel. Not composed: only its host listens. */
export class SearchManagerChipEditCancelEvent extends Event {
  /** The event type. */
  static readonly TYPE = "search-manager-chip-edit-cancel";

  constructor() {
    super(SearchManagerChipEditCancelEvent.TYPE, { bubbles: true });
  }
}

declare global {
  interface HTMLElementEventMap {
    [SearchManagerFiltersChangeEvent.TYPE]: SearchManagerFiltersChangeEvent;
    [SearchManagerAddFilterEvent.TYPE]: SearchManagerAddFilterEvent;
    [SearchManagerChipChangeEvent.TYPE]: SearchManagerChipChangeEvent;
    [SearchManagerChipRemoveEvent.TYPE]: SearchManagerChipRemoveEvent;
    [SearchManagerChipEditCancelEvent.TYPE]: SearchManagerChipEditCancelEvent;
  }
}
