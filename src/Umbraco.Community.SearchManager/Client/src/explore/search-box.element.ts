import { css, customElement, html, property, query, state } from "@umbraco-cms/backoffice/external/lit";
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
import { appendFilters, disabledReason, removeFilterAt, replaceFilterAt } from "./filters.js";
import { searchKeyAction } from "./search-keys.js";
import "./filter-chip.element.js";

/**
 * The Explore search box: one `uui-input` with the chips in its `prepend` slot.
 *
 * - **Enter** sends the input to `POST /explore/{target}/parse` (the one parser, in Core) and
 *   appends the chips it returns, skipping exact duplicates; an unbalanced quote still adds the
 *   text and a notification says why. A newer Enter aborts a parse in flight.
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

  @query("#input")
  private _input?: UUIInputElement;

  #notifications?: UmbNotificationContext;
  #parse?: AbortController;

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
  }

  #emit(filters: Array<Filter>): void {
    this.dispatchEvent(new SearchManagerFiltersChangeEvent(filters));
  }

  #onKeydown(event: KeyboardEvent): void {
    // Keys from the chips (their buttons and editors sit in the prepend slot) are not ours.
    if (event.target !== this._input) return;
    switch (searchKeyAction(event, this._value, this.filters.length)) {
      case "submit":
        event.preventDefault();
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
        @input=${(event: Event) => (this._value = String((event.target as UUIInputElement).value ?? ""))}
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
    `;
  }

  static override styles = [
    css`
      :host {
        display: block;
        min-width: 0;
        container-type: inline-size;
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
