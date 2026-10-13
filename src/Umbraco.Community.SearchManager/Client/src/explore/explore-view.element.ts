import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import type { SelectOption } from "../shared/backoffice-floor.js";
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
import "./score-compare.element.js";
import type { CompareDocument } from "./score-compare.element.js";
import type { SentQuery } from "./score-explain.js";
import "./fields-panel.element.js";
import "./results.element.js";
import "./search-box.element.js";
import "./native-box.element.js";
import "./show-query-panel.element.js";
import { chipsAfterNative, nativeStart } from "./native-mode.js";

/** The target Explore opens on when the URL names none. */
const DEFAULT_TARGET = "ExternalIndex";

/**
 * What a change of search closes: the open document, its score explanation and a comparison,
 * all of which belong to the previous results.
 */
const CLOSED: Pick<ExploreViewState, "doc" | "explain" | "compare"> = {
  doc: undefined,
  explain: undefined,
  compare: undefined,
};

/**
 * The Explore tab: search an index or searcher with filter chips.
 *
 * - **Pickers:** the targets from `GET /explore/targets` (indexes, then searchers registered on
 *   their own) and the site's cultures; the culture switches culture-variant fields.
 * - **Search box:** chips from `POST /explore/{target}/parse`; chips the target cannot run are
 *   drawn disabled and not sent.
 * - **Results:** `POST /explore/{target}/search` on every change of target, culture, chips or
 *   page; a newer search aborts the one in flight. The drawer shows the open document's fields.
 * - **Native mode:** "Lucene" (offered for Lucene searchers only) moves the last query into a raw
 *   Lucene box; "Back to filters" turns it into chips again with `POST /read-native`. While it is
 *   on, searches send the raw query instead of the chips.
 * - **Show query:** the panel shows the query sent, as parsed, and as C#.
 * - **Scores:** the drawer's "Explain score" and the results' "Compare scores" run the query the
 *   last search sent ({@link _query}) again through `POST /explore/{target}/explain`; the
 *   comparison takes the drawer's place.
 * - **URL:** the whole view (index, culture, chips, raw query, columns, open document, page, show
 *   query, explanation, comparison) is written to the query string with `replaceState` and read back when the tab opens,
 *   so a link reopens it.
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

  /**
   * The query the last search sent, for explanations: their `filterIndex` and `mustNotMatch`
   * index into its `filters`. Kept as the same object while it does not change (paging), so the
   * drawer does not explain again.
   */
  @state()
  private _query?: SentQuery;

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
    // Native mode on a target that has it runs the raw query; otherwise the chips that can run.
    const native = target.capabilities.nativeQuery ? this._state.native : undefined;
    const filters = this._state.filters.filter((filter) => !disabledReason(filter, this._schema, target, term));
    // A new array only when the chips changed, so the fields panel does not count again on paging.
    // The fields panel counts over the same query: in native mode, the raw query as one chip.
    const sent: Array<Filter> =
      native === undefined
        ? filters
        : native.trim()
          ? [{ kind: "Lucene", field: null, op: "Contains", value: native, exclude: false }]
          : [];
    if (JSON.stringify(sent) !== JSON.stringify(this._sent)) this._sent = sent;
    // The request body without paging; the server's filter indexes refer to its `filters`.
    const query: SentQuery = {
      filters: native === undefined ? filters : [],
      native: native ?? null,
      culture: this._state.culture ?? null,
    };
    if (JSON.stringify(query) !== JSON.stringify(this._query)) this._query = query;
    this._loading = true;
    const { data, error, response } = await ExploreService.searchExplore({
      path: { target: target.name },
      body: {
        ...query,
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
    const searchKeys: Array<keyof ExploreViewState> = ["filters", "culture", "page", "native"];
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

  /**
   * Opens native mode with the query the last search ran. Chips that could not run were never
   * part of it, so they are kept to come back when native mode closes.
   */
  #enterNative(): void {
    this.#update({ native: nativeStart(this._result?.query), page: 1, ...CLOSED });
  }

  /** Closes native mode, turning the raw query back into chips. */
  async #leaveNative(): Promise<void> {
    const target = this.#target;
    const native = this._state.native ?? "";
    if (!target) return;
    const term = (key: string, ...args: Array<string>) => this.localize.term(key, ...args);
    const disabled = this._state.filters.filter((filter) => disabledReason(filter, this._schema, target, term));
    if (!native.trim()) {
      this.#update({ native: undefined, filters: disabled, page: 1 });
      return;
    }

    const { data, error, response } = await ExploreService.readNativeQuery({
      path: { target: target.name },
      body: { query: native },
    });
    if (!data) {
      this._error = this.localize.term("searchManager_nativeReadError", describeError(error, response));
      return;
    }
    this.#update({ native: undefined, filters: chipsAfterNative(data.filters, disabled), page: 1, ...CLOSED });
  }

  /**
   * Adds (or toggles) a chip from the fields panel or the drawer. In native mode the raw query
   * is turned back into chips first, so the new chip joins them rather than a hidden list.
   */
  async #addFilter(event: SearchManagerAddFilterEvent): Promise<void> {
    if (this._state.native !== undefined && this.#target?.capabilities.nativeQuery) {
      await this.#leaveNative();
      if (this._state.native !== undefined) return;
    }
    this.#setFilters(
      event.toggle
        ? toggleFilter(this._state.filters, event.filter)
        : appendFilters(this._state.filters, [event.filter]),
    );
  }

  #setFilters(filters: Array<Filter>): void {
    this.#update({ filters, page: 1, ...CLOSED });
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
    const native = this._state.native !== undefined && (target?.capabilities.nativeQuery ?? false);
    const openHit = this._result?.hits.find((hit) => hit.id === this._state.doc);
    const compared = this.#comparedDocuments();
    return html`
      <uui-box class="query">
        <div class="pickers">${this.#renderTargetPicker(target)} ${this.#renderCulturePicker()}</div>
        <div class="query-row">
          ${
            native
              ? html`<search-manager-native-box
                  .target=${target}
                  .query=${this._state.native ?? ""}
                  @search-manager-native-run=${(event: CustomEvent<string>) =>
                    this.#update({ native: event.detail, page: 1, ...CLOSED })}
                ></search-manager-native-box>`
              : html`<search-manager-search-box
                  .target=${target}
                  .schema=${this._schema}
                  .filters=${this._state.filters}
                  @search-manager-filters-change=${(event: SearchManagerFiltersChangeEvent) =>
                    this.#setFilters(event.filters)}
                ></search-manager-search-box>`
          }
          ${this.#renderNativeToggle(target, native)}
          <uui-button
            look=${this._state.showQuery ? "primary" : "secondary"}
            label=${this.localize.term(this._state.showQuery ? "searchManager_hideQuery" : "searchManager_showQuery")}
            aria-pressed=${this._state.showQuery ? "true" : "false"}
            @click=${() => this.#update({ showQuery: !this._state.showQuery || undefined })}
          >
            <umb-icon name="icon-code"></umb-icon>
          </uui-button>
        </div>
        ${this._error ? html`<p class="error" role="alert">${this._error}</p>` : nothing}
        ${
          this._state.showQuery && target
            ? html`<search-manager-show-query-panel
                .target=${target}
                .schema=${this._schema}
                .filters=${native ? [] : this._sent}
                .notSent=${native ? [] : this.#notSent(target)}
                .culture=${this._state.culture}
                .native=${native ? (this._state.native ?? "") : undefined}
              ></search-manager-show-query-panel>`
            : nothing
        }
      </uui-box>
      ${this._loading && !this._result ? html`<uui-loader-bar></uui-loader-bar>` : nothing}
      <div
        class=${openHit || compared ? "main drawer-open" : "main"}
        @search-manager-add-filter=${(event: SearchManagerAddFilterEvent) => void this.#addFilter(event)}
        @search-manager-open-document=${(event: CustomEvent<string | undefined>) =>
          // Another document keeps its explanation open; closing the drawer closes it.
          this.#update({
            doc: event.detail,
            explain: event.detail ? this._state.explain : undefined,
            compare: undefined,
          })}
        @search-manager-explain-toggle=${(event: CustomEvent<boolean>) =>
          this.#update({ explain: event.detail || undefined })}
        @search-manager-compare=${(event: CustomEvent<[string, string] | undefined>) =>
          this.#update(
            event.detail ? { compare: event.detail, doc: undefined, explain: undefined } : { compare: undefined },
          )}
      >
        <search-manager-fields-panel
          .target=${target}
          .schema=${this._schema}
          .filters=${this._sent}
          .allFilters=${native ? [] : this._state.filters}
          .culture=${this._state.culture}
        ></search-manager-fields-panel>
        <uui-box class="results">
          <search-manager-results
            .result=${this._result}
            .schema=${this._schema}
            .columns=${this._state.columns}
            .page=${this._state.page}
            .openDocument=${this._state.doc}
            .canCompare=${target?.capabilities.explainScore ?? false}
            .compare=${this._state.compare}
            @search-manager-page-change=${(event: CustomEvent<number>) => this.#update({ page: event.detail, ...CLOSED })}
            @search-manager-columns-change=${(event: CustomEvent<Array<string>>) => this.#update({ columns: event.detail })}
          ></search-manager-results>
        </uui-box>
        ${
          compared
            ? html`<search-manager-score-compare
                aria-label=${this.localize.term("searchManager_compareTitle")}
                .target=${target}
                .query=${this._query}
                .documents=${compared}
                .schema=${this._schema}
              ></search-manager-score-compare>`
            : openHit
              ? html`<search-manager-document-drawer
                  aria-label=${this.localize.term("searchManager_documentLabel")}
                  .hit=${openHit}
                  .schema=${this._schema}
                  .target=${target}
                  .query=${this._query}
                  ?explain=${this._state.explain ?? false}
                ></search-manager-document-drawer>`
              : nothing
        }
      </div>
    `;
  }

  /**
   * The two documents being compared, named from the current results; one that is not on this
   * page (a shared link) is named by its id.
   */
  #comparedDocuments(): [CompareDocument, CompareDocument] | undefined {
    const ids = this._state.compare;
    if (!ids) return undefined;
    const named = (id: string): CompareDocument => ({
      id,
      name: this._result?.hits.find((hit) => hit.id === id)?.name ?? id,
    });
    return [named(ids[0]), named(ids[1])];
  }

  /** The chips left out of the query, with why, for the show-query panel. */
  #notSent(target: ExploreTarget): Array<{ filter: Filter; reason: string }> {
    const term = (key: string, ...args: Array<string>) => this.localize.term(key, ...args);
    const fromServer = new Map((this._result?.skipped ?? []).map((skip) => [skip.index, skip.reason]));
    let sentIndex = 0;
    return this._state.filters.flatMap((filter) => {
      const reason = disabledReason(filter, this._schema, target, term);
      if (reason) return [{ filter, reason }];
      // The server's skipped list indexes the chips it was sent.
      const serverReason = fromServer.get(sentIndex++);
      return serverReason ? [{ filter, reason: serverReason }] : [];
    });
  }

  /** "Lucene" / "Back to filters", disabled with the reason on a target without raw queries. */
  #renderNativeToggle(target: ExploreTarget | undefined, native: boolean) {
    if (native) {
      return html`<uui-button
        look="primary"
        label=${this.localize.term("searchManager_nativeOff")}
        @click=${() => void this.#leaveNative()}
      ></uui-button>`;
    }
    const available = target?.capabilities.nativeQuery ?? false;
    return html`<uui-button
      look="secondary"
      label=${this.localize.term("searchManager_nativeOn")}
      title=${
        available
          ? this.localize.term("searchManager_nativeOnLabel")
          : this.localize.term("searchManager_nativeUnavailable", target?.name ?? "")
      }
      ?disabled=${!available}
      @click=${() => this.#enterNative()}
    ></uui-button>`;
  }

  #renderTargetPicker(target: ExploreTarget | undefined) {
    const options: Array<SelectOption> = (this._targets ?? []).map((candidate) => ({
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
              ...CLOSED,
              columns: [],
              native: undefined,
            })}
        ></uui-select>
      </div>
    `;
  }

  #renderCulturePicker() {
    const cultures = this._schema?.cultures ?? [];
    if (cultures.length === 0) return nothing;
    const options: Array<SelectOption> = [
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

      .query-row {
        display: flex;
        gap: var(--uui-size-space-2);
        align-items: flex-start;
      }

      .query-row > :first-child {
        flex: 1;
        min-width: 0;
      }

      search-manager-show-query-panel {
        margin-top: var(--uui-size-space-4);
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

      search-manager-document-drawer,
      search-manager-score-compare {
        flex: 1 1 24rem;
        max-width: 36rem;
      }

      /*
       * The fields panel, the results and the drawer only fit on one line from about 73rem
       * (16 + 30 + 24 plus the gaps). Narrower, flex-wrap would push the drawer below the results,
       * out of sight; move the fields panel to the end instead, so the drawer stays beside the
       * results it belongs to.
       */
      .main {
        container-type: inline-size;
      }

      @container (max-width: 73rem) {
        .main.drawer-open search-manager-fields-panel {
          order: 1;
        }
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
