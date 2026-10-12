import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import type { UUISelectOption } from "@umbraco-cms/backoffice/external/uui";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import {
  ExploreService,
  type ExploreResult,
  type ExploreSchema,
  type ExploreTarget,
  type Filter,
} from "../api/index.js";
import { describeError } from "../shared/describe-error.js";
import "../shared/empty-state.element.js";
import { SearchManagerAddFilterEvent, SearchManagerFiltersChangeEvent } from "./explore-events.js";
import {
  createExploreState,
  decodeExploreState,
  encodeExploreState,
  mergeIntoSearch,
  PAGE_SIZE,
  type ExploreViewState,
} from "./explore-state.js";
import { appendFilters, disabledReason, toggleFilter } from "./filters.js";
import "./document-drawer.element.js";
import "./fields-panel.element.js";
import "./results.element.js";
import "./search-box.element.js";

/** The target Explore opens on when the URL names none. */
const DEFAULT_TARGET = "ExternalIndex";

/**
 * The Explore tab: search an index or searcher with filter chips.
 *
 * - **Pickers:** the targets from `GET /explore/targets` (indexes, then searchers registered on
 *   their own) and the site's cultures; the culture switches culture-variant fields.
 * - **Search box:** chips from `POST /explore/{target}/parse`; chips the target cannot run are
 *   drawn disabled and not sent.
 * - **Results:** `POST /explore/{target}/search` on every change of target, culture, chips or
 *   page; a newer search aborts the one in flight. The drawer shows the open document's fields.
 * - **URL:** the whole view (index, culture, chips, columns, open document, page) is written to
 *   the query string with `replaceState` and read back when the tab opens, so a link reopens it.
 *
 * Bound by the `Umbraco.Community.SearchManager.WorkspaceView.Explore` manifest.
 *
 * @element search-manager-explore-view
 */
@customElement("search-manager-explore-view")
export class SearchManagerExploreViewElement extends UmbLitElement {
  @state()
  private _state: ExploreViewState = createExploreState();

  @state()
  private _targets?: Array<ExploreTarget>;

  @state()
  private _schema?: ExploreSchema;

  @state()
  private _result?: ExploreResult;

  @state()
  private _loading = false;

  @state()
  private _error?: string;

  /** The chips the last search sent; kept as the same array while they do not change. */
  @state()
  private _sent: Array<Filter> = [];

  #search?: AbortController;
  #schemaFor?: string;

  override connectedCallback(): void {
    super.connectedCallback();
    this._state = decodeExploreState(new URLSearchParams(window.location.search));
    void this.#loadTargets();
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#search?.abort();
  }

  /**
   * The target in use: the one in the state when it is listed, otherwise ExternalIndex (the
   * published content most searches are about), otherwise the first.
   */
  get #target(): ExploreTarget | undefined {
    return (
      this._targets?.find((target) => target.name === this._state.target) ??
      this._targets?.find((target) => target.name === DEFAULT_TARGET) ??
      this._targets?.[0]
    );
  }

  async #loadTargets(): Promise<void> {
    const { data, error, response } = await ExploreService.getExploreTargets();
    if (!data) {
      this._error = this.localize.term("searchManager_exploreTargetsError", describeError(error, response));
      return;
    }
    this._targets = data;
    await this.#loadSchema();
  }

  async #loadSchema(): Promise<void> {
    const target = this.#target;
    if (!target || this.#schemaFor === target.name) return;
    this.#schemaFor = target.name;
    this._schema = undefined;
    const { data, error, response } = await ExploreService.getExploreSchema({ path: { target: target.name } });
    // A newer target may have been picked while this one loaded.
    if (this.#schemaFor !== target.name) return;
    if (!data) {
      this._error = this.localize.term("searchManager_exploreSchemaError", describeError(error, response));
      return;
    }
    this._schema = data;
    await this.#runSearch();
  }

  /** Runs the search for the current state, sending only the chips the target can run. */
  async #runSearch(): Promise<void> {
    const target = this.#target;
    if (!target || !this._schema) return;
    this.#search?.abort();
    const controller = new AbortController();
    this.#search = controller;

    const term = (key: string, ...args: Array<string>) => this.localize.term(key, ...args);
    const filters = this._state.filters.filter((filter) => !disabledReason(filter, this._schema, target, term));
    // A new array only when the chips changed, so the fields panel does not count again on paging.
    if (JSON.stringify(filters) !== JSON.stringify(this._sent)) this._sent = filters;
    this._loading = true;
    const { data, error, response } = await ExploreService.searchExplore({
      path: { target: target.name },
      body: {
        filters,
        culture: this._state.culture ?? null,
        skip: (this._state.page - 1) * PAGE_SIZE,
        take: PAGE_SIZE,
      },
      signal: controller.signal,
    }).catch((reason: unknown) => ({ data: undefined, error: reason, response: undefined }));
    if (controller.signal.aborted) return;
    this._loading = false;
    if (data) {
      this._result = data;
      this._error = undefined;
    } else {
      this._error = this.localize.term("searchManager_searchError", describeError(error, response));
    }
  }

  /**
   * Applies a change to the view: writes the URL, and searches again unless only the columns or
   * the open document changed.
   */
  #update(partial: Partial<ExploreViewState>): void {
    const previous = this._state;
    this._state = { ...previous, ...partial };
    this.#writeUrl();

    if (partial.target !== undefined && partial.target !== previous.target) {
      void this.#loadSchema();
      return;
    }
    const searchKeys: Array<keyof ExploreViewState> = ["filters", "culture", "page"];
    if (searchKeys.some((key) => key in partial)) void this.#runSearch();
  }

  #writeUrl(): void {
    // Only while this tab is shown: the other tabs own their own query strings.
    if (!window.location.pathname.includes("/explore")) return;
    const search = mergeIntoSearch(window.location.search, encodeExploreState(this._state));
    if (search === window.location.search.replace(/^\?/, "")) return;
    const url = `${window.location.pathname}${search ? `?${search}` : ""}${window.location.hash}`;
    window.history.replaceState(window.history.state, "", url);
  }

  #setFilters(filters: Array<Filter>): void {
    this.#update({ filters, page: 1, doc: undefined });
  }

  /**
   * Renders the pickers, the search box, the results and the drawer.
   *
   * @returns The template.
   */
  override render() {
    if (this._targets && this._targets.length === 0) {
      return html`<search-manager-empty-state headline=${this.localize.term("searchManager_tabExplore")}>
        <p>${this.localize.term("searchManager_exploreNoTargets")}</p>
      </search-manager-empty-state>`;
    }

    const target = this.#target;
    const openHit = this._result?.hits.find((hit) => hit.id === this._state.doc);
    return html`
      <uui-box class="query">
        <div class="pickers">${this.#renderTargetPicker(target)} ${this.#renderCulturePicker()}</div>
        <search-manager-search-box
          .target=${target}
          .schema=${this._schema}
          .filters=${this._state.filters}
          @search-manager-filters-change=${(event: SearchManagerFiltersChangeEvent) => this.#setFilters(event.filters)}
        ></search-manager-search-box>
        ${this._error ? html`<p class="error" role="alert">${this._error}</p>` : nothing}
      </uui-box>
      ${this._loading && !this._result ? html`<uui-loader-bar></uui-loader-bar>` : nothing}
      <div
        class="main"
        @search-manager-add-filter=${(event: SearchManagerAddFilterEvent) =>
          this.#setFilters(
            event.toggle
              ? toggleFilter(this._state.filters, event.filter)
              : appendFilters(this._state.filters, [event.filter]),
          )}
        @search-manager-open-document=${(event: CustomEvent<string | undefined>) => this.#update({ doc: event.detail })}
      >
        <search-manager-fields-panel
          .target=${target}
          .schema=${this._schema}
          .filters=${this._sent}
          .allFilters=${this._state.filters}
          .culture=${this._state.culture}
        ></search-manager-fields-panel>
        <uui-box class="results">
          <search-manager-results
            .result=${this._result}
            .schema=${this._schema}
            .columns=${this._state.columns}
            .page=${this._state.page}
            .openDocument=${this._state.doc}
            @search-manager-page-change=${(event: CustomEvent<number>) => this.#update({ page: event.detail, doc: undefined })}
            @search-manager-columns-change=${(event: CustomEvent<Array<string>>) => this.#update({ columns: event.detail })}
          ></search-manager-results>
        </uui-box>
        ${
          openHit
            ? html`<search-manager-document-drawer
                aria-label=${this.localize.term("searchManager_documentLabel")}
                .hit=${openHit}
                .schema=${this._schema}
                .targetName=${target?.name ?? ""}
              ></search-manager-document-drawer>`
            : nothing
        }
      </div>
    `;
  }

  #renderTargetPicker(target: ExploreTarget | undefined) {
    const options: Array<UUISelectOption> = (this._targets ?? []).map((candidate) => ({
      name:
        candidate.kind === "Searcher"
          ? this.localize.term("searchManager_exploreSearcher", candidate.name)
          : candidate.name,
      value: candidate.name,
      selected: candidate.name === target?.name,
    }));
    return html`
      <div class="picker">
        <uui-label for="target">${this.localize.term("searchManager_exploreIndex")}</uui-label>
        <uui-select
          id="target"
          label=${this.localize.term("searchManager_exploreIndex")}
          .options=${options}
          @change=${(event: Event) =>
            this.#update({
              target: (event.target as HTMLSelectElement).value,
              page: 1,
              doc: undefined,
              columns: [],
            })}
        ></uui-select>
      </div>
    `;
  }

  #renderCulturePicker() {
    const cultures = this._schema?.cultures ?? [];
    if (cultures.length === 0) return nothing;
    const options: Array<UUISelectOption> = [
      { name: this.localize.term("searchManager_exploreAllCultures"), value: "", selected: !this._state.culture },
      ...cultures.map((culture) => ({ name: culture, value: culture, selected: culture === this._state.culture })),
    ];
    return html`
      <div class="picker">
        <uui-label for="culture">${this.localize.term("searchManager_exploreCulture")}</uui-label>
        <uui-select
          id="culture"
          label=${this.localize.term("searchManager_exploreCulture")}
          .options=${options}
          @change=${(event: Event) =>
            this.#update({ culture: (event.target as HTMLSelectElement).value || undefined, page: 1 })}
        ></uui-select>
      </div>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-4);
        padding: var(--uui-size-layout-1);
      }

      .query {
        display: block;
      }

      .pickers {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-5);
        margin-bottom: var(--uui-size-space-4);
      }

      .picker {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .main {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-4);
        align-items: flex-start;
      }

      .results {
        flex: 999 1 30rem;
        min-width: 0;
      }

      search-manager-fields-panel {
        flex: 1 1 16rem;
        max-width: 22rem;
      }

      search-manager-document-drawer {
        flex: 1 1 24rem;
        max-width: 36rem;
      }

      .error {
        color: var(--uui-color-danger);
      }
    `,
  ];
}

export { SearchManagerExploreViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-explore-view": SearchManagerExploreViewElement;
  }
}
