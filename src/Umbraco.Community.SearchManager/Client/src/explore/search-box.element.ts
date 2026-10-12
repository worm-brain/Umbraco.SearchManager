import { css, customElement, html, nothing, property, query, state } from "@umbraco-cms/backoffice/external/lit";
import type { UUIInputElement } from "@umbraco-cms/backoffice/external/uui";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UMB_NOTIFICATION_CONTEXT, type UmbNotificationContext } from "@umbraco-cms/backoffice/notification";
import { ExploreService, type ExploreSchema, type ExploreTarget, type Filter } from "../api/index.js";
import { describeError } from "../shared/describe-error.js";
import {
  SearchManagerChipChangeEvent,
  SearchManagerChipRemoveEvent,
  SearchManagerFiltersChangeEvent,
} from "./explore-events.js";
import { appendFilters, disabledReason, removeFilterAt, replaceFilterAt, resolveField } from "./filters.js";
import { searchKeyAction } from "./search-keys.js";
import {
  dateLabel,
  fieldSuggestions,
  moveHighlight,
  readInput,
  valueSuggestions,
  type Suggestion,
} from "./suggestions.js";

/** Wait after the last keystroke before asking the server for values. */
const TERMS_DEBOUNCE_MS = 150;
import "./filter-chip.element.js";

/**
 * The Explore search box: one `uui-input` with the chips in its `prepend` slot.
 *
 * - **Enter** sends the input to `POST /explore/{target}/parse` (the one parser, in Core) and
 *   appends the chips it returns, skipping exact duplicates; an unbalanced quote still adds the
 *   text and a notification says why. A newer Enter aborts a parse in flight.
 * - **Suggestions** open under the box as the user types: fields (with type and coverage), then,
 *   after `field:`, the field's values from `GET /explore/{target}/terms` (relative ranges for
 *   dates). Arrow keys move, Enter picks the highlighted row (Enter with none highlighted parses
 *   the text), Escape closes the list.
 * - **Backspace** in an empty input removes the last chip; **Escape** clears the input.
 * - **Chips** open their editor on click; a chip the target cannot run is drawn disabled.
 *
 * The box does not own the chips: every change is a {@link SearchManagerFiltersChangeEvent}
 * that the Explore view applies and passes back down.
 *
 * @element search-manager-search-box
 * @fires search-manager-filters-change - With the new chip list.
 */
@customElement("search-manager-search-box")
export class SearchManagerSearchBoxElement extends UmbLitElement {
  /** The index or searcher being searched. */
  @property({ attribute: false })
  target?: ExploreTarget;

  /** Its schema, for chip labels and disabled chips. */
  @property({ attribute: false })
  schema?: ExploreSchema;

  /** The chips. */
  @property({ attribute: false })
  filters: Array<Filter> = [];

  /** The text in the input. */
  @state()
  private _value = "";

  /** The suggestion rows. */
  @state()
  private _suggestions: Array<Suggestion> = [];

  /** Whether the suggestion list is open. */
  @state()
  private _open = false;

  /** Whether the server stopped reading the field's terms at its limit. */
  @state()
  private _sampled = false;

  /** The highlighted row, or -1. */
  @state()
  private _highlight = -1;

  @query("#input")
  private _input?: UUIInputElement;

  #notifications?: UmbNotificationContext;
  #parse?: AbortController;
  #terms?: AbortController;
  #termsTimer?: ReturnType<typeof setTimeout>;
  #blurTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));
  }

  /** Moves focus to the text input. */
  override focus(): void {
    void this._input?.focus();
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#parse?.abort();
    this.#terms?.abort();
    clearTimeout(this.#termsTimer);
    clearTimeout(this.#blurTimer);
  }

  /**
   * Recomputes the suggestions for the input: field rows at once, value rows after a short
   * wait for the server (dates need none).
   */
  #suggest(): void {
    const schema = this.schema;
    this._highlight = -1;
    this._sampled = false;
    this.#terms?.abort();
    clearTimeout(this.#termsTimer);
    if (!schema || !this.target) {
      this._suggestions = [];
      return;
    }

    const input = readInput(this._value);
    if (input.field === undefined) {
      this._suggestions = this._value.includes(" ") ? [] : fieldSuggestions(schema, input);
      return;
    }

    // Dates suggest fixed relative ranges; other fields need the server, unless the target's
    // terms cannot be read, when there is nothing to suggest.
    const field = resolveField(schema, input.field);
    if (!field || field.type === "Date" || !this.target.capabilities.termStatistics) {
      this._suggestions = valueSuggestions(schema, input, []);
      return;
    }

    const target = this.target.name;
    this.#termsTimer = setTimeout(async () => {
      const controller = new AbortController();
      this.#terms = controller;
      const { data } = await ExploreService.getExploreTerms({
        path: { target },
        query: { field: field.name, prefix: input.prefix, take: 8 },
        signal: controller.signal,
      }).catch(() => ({ data: undefined }));
      if (controller.signal.aborted || !data || !this.schema) return;
      this._suggestions = valueSuggestions(this.schema, readInput(this._value), data.terms);
      this._sampled = data.sampled;
    }, TERMS_DEBOUNCE_MS);
  }

  #pick(row: Suggestion): void {
    if (row.kind === "field") {
      this.#setValue(row.insert);
      this.#suggest();
      this.focus();
      return;
    }
    this.#emit(appendFilters(this.filters, [row.filter]));
    this.#setValue("");
    this._open = false;
    this.focus();
  }

  #emit(filters: Array<Filter>): void {
    this.dispatchEvent(new SearchManagerFiltersChangeEvent(filters));
  }

  #onKeydown(event: KeyboardEvent): void {
    // Keys from the chips (their buttons and editors sit in the prepend slot) are not ours.
    if (event.target !== this._input) return;

    // The suggestion list takes the arrow keys, Enter on a highlighted row, and Escape.
    if (this._open && this._suggestions.length > 0) {
      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        event.preventDefault();
        this._highlight = moveHighlight(this._highlight, event.key === "ArrowDown" ? 1 : -1, this._suggestions.length);
        return;
      }
      const row = this._suggestions[this._highlight];
      if (event.key === "Enter" && !event.isComposing && row) {
        event.preventDefault();
        this.#pick(row);
        return;
      }
      if (event.key === "Escape") {
        event.preventDefault();
        event.stopPropagation();
        this._open = false;
        return;
      }
    }

    switch (searchKeyAction(event, this._value, this.filters.length)) {
      case "submit":
        event.preventDefault();
        this._open = false;
        void this.#submit(this._value);
        break;
      case "removeLastChip":
        event.preventDefault();
        this.#emit(removeFilterAt(this.filters, this.filters.length - 1));
        break;
      case "clear":
        event.preventDefault();
        event.stopPropagation();
        this.#setValue("");
        break;
    }
  }

  async #submit(input: string): Promise<void> {
    if (!this.target) return;
    this.#parse?.abort();
    const controller = new AbortController();
    this.#parse = controller;

    const { data, error, response } = await ExploreService.parseExploreInput({
      path: { target: this.target.name },
      body: { input },
      signal: controller.signal,
    }).catch((reason: unknown) => ({ data: undefined, error: reason, response: undefined }));
    if (controller.signal.aborted) return;

    if (!data) {
      this.#notifications?.peek("danger", {
        data: { message: this.localize.term("searchManager_searchParseError", describeError(error, response)) },
      });
      return;
    }

    this.#emit(appendFilters(this.filters, data.filters));
    // Clear only what was parsed: the user may have typed on while the request ran.
    if (this._value === input) this.#setValue("");
    if (data.fallback) {
      this.#notifications?.peek("warning", {
        data: { message: this.localize.term("searchManager_searchUnbalancedQuote") },
      });
    }
  }

  #setValue(value: string): void {
    // Written to the element too: Lit sees no change when the mirror already holds "".
    this._value = value;
    if (this._input) this._input.value = value;
  }

  /**
   * Renders the input with the chips before the text.
   *
   * @returns The template.
   */
  override render() {
    const term = (key: string, ...args: Array<string>) => this.localize.term(key, ...args);
    return html`
      <uui-input
        id="input"
        label=${this.localize.term("searchManager_searchLabel")}
        placeholder=${this.localize.term(
          this.filters.length > 0 ? "searchManager_searchPlaceholderMore" : "searchManager_searchPlaceholder",
        )}
        autocomplete="off"
        spellcheck="false"
        .value=${this._value}
        @input=${(event: Event) => {
          this._value = String((event.target as UUIInputElement).value ?? "");
          this._open = true;
          this.#suggest();
        }}
        @focus=${() => {
          clearTimeout(this.#blurTimer);
          this._open = true;
          this.#suggest();
        }}
        @blur=${() => (this.#blurTimer = setTimeout(() => (this._open = false), 150))}
        @keydown=${this.#onKeydown}
      >
        <div slot="prepend" class="prepend">
          <umb-icon name="icon-search" aria-hidden="true"></umb-icon>
          <div class="chips">
            ${this.filters.map(
              (filter, index) => html`
                <search-manager-filter-chip
                  .filter=${filter}
                  .schema=${this.schema}
                  .disabledReason=${disabledReason(filter, this.schema, this.target, term) ?? ""}
                  @search-manager-chip-change=${(event: SearchManagerChipChangeEvent) => {
                    event.stopPropagation();
                    this.#emit(replaceFilterAt(this.filters, index, event.filter));
                  }}
                  @search-manager-chip-remove=${(event: SearchManagerChipRemoveEvent) => {
                    event.stopPropagation();
                    this.#emit(removeFilterAt(this.filters, index));
                    this.focus();
                  }}
                ></search-manager-filter-chip>
              `,
            )}
          </div>
        </div>
      </uui-input>
      ${this.#renderSuggestions()}
    `;
  }

  /** The suggestion list: field rows with type and coverage, value rows with counts. */
  #renderSuggestions() {
    if (!this._open || this._suggestions.length === 0) return nothing;
    const term = (key: string, ...args: Array<string>) => this.localize.term(key, ...args);
    const input = readInput(this._value);
    const title =
      input.field === undefined
        ? this.localize.term("searchManager_suggestFields")
        : this.localize.term("searchManager_suggestValues", input.field);
    return html`
      <div class="suggestions">
        <div class="title" id="suggestions-title">${title}</div>
        <ul role="listbox" aria-labelledby="suggestions-title">
          ${this._suggestions.map((row, index) => {
            const main =
              row.kind === "field"
                ? row.insert
                : row.kind === "value"
                  ? `${row.filter.exclude ? "-" : ""}${input.field}:${row.label}`
                  : row.label;
            const detail =
              row.kind === "field"
                ? row.label === row.field.name
                  ? ""
                  : row.field.name
                : row.kind === "value" && row.term
                  ? this.localize.term("searchManager_suggestTerm")
                  : row.kind === "value" && dateLabel(row.label, term) !== row.label
                    ? dateLabel(row.label, term)
                    : row.kind === "text"
                      ? this.localize.term("searchManager_suggestText")
                      : "";
            const badge = row.kind === "field" ? row.field.type : row.kind === "text" ? "Text" : "";
            const count = row.kind === "field" ? row.coverage : row.kind === "value" ? row.count : "";
            return html`<li
              role="option"
              aria-selected=${index === this._highlight ? "true" : "false"}
              class=${index === this._highlight ? "highlight" : ""}
              @mousedown=${(event: Event) => {
                // Keeps focus in the input, so blur does not close the list first.
                event.preventDefault();
                this.#pick(row);
              }}
            >
              <span class="main">${main}</span>
              <span class="detail">${detail}</span>
              ${badge ? html`<uui-tag look="secondary">${badge}</uui-tag>` : nothing}
              <span class="count">${count}</span>
            </li>`;
          })}
        </ul>
        ${this._sampled ? html`<div class="hint">${this.localize.term("searchManager_suggestSampled")}</div>` : nothing}
        <div class="hint">${this.localize.term("searchManager_suggestHint")}</div>
      </div>
    `;
  }

  static override styles = [
    css`
      :host {
        display: block;
        min-width: 0;
        container-type: inline-size;
        position: relative;
      }

      .suggestions {
        position: absolute;
        z-index: 2;
        top: calc(100% + var(--uui-size-space-1));
        left: 0;
        width: min(36rem, 100%);
        background: var(--uui-color-surface);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        box-shadow: var(--uui-shadow-depth-3);
        padding: var(--uui-size-space-2) 0;
      }

      .suggestions .title,
      .suggestions .hint {
        padding: var(--uui-size-space-1) var(--uui-size-space-4);
        font-size: var(--uui-type-small-size);
        color: var(--uui-color-text-alt);
      }

      .suggestions .hint {
        border-top: 1px solid var(--uui-color-divider);
        margin-top: var(--uui-size-space-1);
      }

      .suggestions ul {
        list-style: none;
        margin: 0;
        padding: 0;
      }

      .suggestions li {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-2) var(--uui-size-space-4);
        cursor: pointer;
      }

      .suggestions li.highlight,
      .suggestions li:hover {
        background: var(--uui-color-surface-emphasis);
      }

      .suggestions .main {
        flex: 1 1 auto;
        min-width: 0;
        font-family: monospace;
        overflow-wrap: anywhere;
      }

      .suggestions .detail,
      .suggestions .count {
        font-size: var(--uui-type-small-size);
        color: var(--uui-color-text-alt);
      }

      .suggestions .count {
        min-width: 3.5rem;
        text-align: right;
        font-variant-numeric: tabular-nums;
      }

      uui-input {
        width: 100%;
        --uui-input-height: auto;
        min-height: var(--uui-size-11);
      }

      .prepend {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        min-width: 0;
        /* Leaves the text input a usable share of the box however many chips there are. */
        max-width: 70cqi;
        padding-left: var(--uui-size-space-3);
        min-height: var(--uui-size-11);
        box-sizing: border-box;
      }

      umb-icon {
        flex: none;
        color: var(--uui-color-text-alt);
      }

      /* Chips wrap onto up to three rows; beyond that the list scrolls. */
      .chips {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-1);
        min-width: 0;
        padding-block: var(--uui-size-space-2);
        max-height: calc(3 * var(--uui-size-8) + 2 * var(--uui-size-space-1));
        overflow-y: auto;
        scrollbar-width: thin;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-search-box": SearchManagerSearchBoxElement;
  }
}
