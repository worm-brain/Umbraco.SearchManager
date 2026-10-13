import {
  css,
  customElement,
  html,
  nothing,
  property,
  state,
  type PropertyValues,
} from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import {
  DiagnoseService,
  ExploreService,
  type Filter,
  type IndexPresence,
  type NodeCulture,
  type NodeMatchResult,
} from "../../api/index.js";
import { describeFilter } from "../../explore/chip-format.js";
import type { SelectOption } from "../../shared/backoffice-floor.js";
import { describeError } from "../../shared/describe-error.js";
import { SearchManagerDiagnoseChangeEvent } from "../diagnose-events.js";
import { clauseLook, clauseOutcome, matchableIndexes, matchTarget, pairClauses, verdictLook } from "./node-model.js";

/**
 * "Why doesn't this query match it?": tests a query against one node in one index that holds it.
 *
 * Pick an index (only those where the node is present) and, for a variant node, a culture; type
 * a query as in Explore and press Enter. The query is parsed into chips with
 * `POST /explore/{target}/parse`, then tested with `POST /diagnose/{target}/nodes/{key}/match`.
 * The overall verdict (matches, does not match, not in the index) stays at the top; below it,
 * each chip with its outcome (matched, not matched, skipped with the reason) and the node's
 * values of the chip's field. Chips on the same field are ORed, which the view notes, since the
 * verdict can then differ from the chips one by one.
 *
 * The query text and culture live in the Diagnose URL (`q`, `culture`), so a link reopens the
 * test and runs it again.
 *
 * Not bound to a manifest; the Find a node tool renders it.
 *
 * @element search-manager-node-match
 * @fires search-manager-diagnose-change - {@link SearchManagerDiagnoseChangeEvent} with `q` when
 *   a query runs and `culture` when the culture changes.
 */
@customElement("search-manager-node-match")
export class SearchManagerNodeMatchElement extends UmbLitElement {
  /** The node's key. */
  @property({ attribute: false })
  nodeKey?: string;

  /** Every index's presence; only those that hold the node can be tested. */
  @property({ attribute: false })
  indexes: Array<IndexPresence> = [];

  /** The node's cultures; empty for an invariant node, which has no culture picker. */
  @property({ attribute: false })
  cultures: Array<NodeCulture> = [];

  /** The query from the URL; when set, it runs as soon as the node is loaded. */
  @property({ attribute: false })
  query?: string;

  /** The culture from the URL. */
  @property({ attribute: false })
  culture?: string;

  /** The index picked; falls back to the first that holds the node. */
  @state()
  private _target?: string;

  /** The culture picked here, ahead of the URL's while the view catches up. */
  @state()
  private _culture?: string;

  /** The text in the query box. */
  @state()
  private _input = "";

  /** The chips the last test sent. */
  @state()
  private _filters: Array<Filter> = [];

  /** The last test's answer. */
  @state()
  private _result?: NodeMatchResult;

  /** Whether a test is running. */
  @state()
  private _running = false;

  /** Why the last test failed, if it did. */
  @state()
  private _error?: string;

  #request?: AbortController;

  /** The node the URL's query last ran for, so it runs once per node. */
  #autoRanFor?: string;

  /**
   * Picks up the URL's query and runs it once the node and its indexes are known.
   *
   * @param changed - The changed properties.
   */
  protected override willUpdate(changed: PropertyValues<this>): void {
    super.willUpdate(changed);
    if (changed.has("query") && this.query !== undefined && !this._result) this._input = this.query;
  }

  /**
   * Runs the URL's query for a newly loaded node.
   *
   * @param changed - The changed properties.
   */
  protected override updated(changed: PropertyValues<this>): void {
    super.updated(changed);
    if (!changed.has("indexes") && !changed.has("nodeKey")) return;
    if (changed.has("nodeKey")) {
      // A different node: the last test and the culture picked for the old one no longer apply.
      this._result = undefined;
      this._error = undefined;
      this._culture = undefined;
    }
    if (this.query && this.nodeKey && this.#autoRanFor !== this.nodeKey && matchableIndexes(this.indexes).length > 0) {
      this.#autoRanFor = this.nodeKey;
      void this.#run(this.query);
    }
  }

  /** Stops a test in flight when the element leaves the page. */
  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#request?.abort();
  }

  /** The index tests run on. */
  get #targetName(): string | undefined {
    return matchTarget(this.indexes, this._target);
  }

  /** The culture tests run in: the one picked when the node has it, otherwise its first. */
  get #culture(): string | undefined {
    if (this.cultures.length === 0) return undefined;
    const wanted = (this._culture ?? this.culture)?.toLowerCase();
    return this.cultures.find((culture) => culture.iso.toLowerCase() === wanted)?.iso ?? this.cultures[0]?.iso;
  }

  /**
   * Parses the query into chips for the index, then tests them against the node.
   *
   * @param input - The query text.
   */
  async #run(input: string): Promise<void> {
    const target = this.#targetName;
    const key = this.nodeKey;
    if (!target || !key) return;
    this.#request?.abort();
    const controller = new AbortController();
    this.#request = controller;
    this._running = true;
    this._error = undefined;

    const failed = (reason: unknown) => ({ data: undefined, error: reason, response: undefined });
    const parsed = await ExploreService.parseExploreInput({
      path: { target },
      body: { input },
      signal: controller.signal,
    }).catch(failed);
    if (controller.signal.aborted) return;
    if (!parsed.data) {
      this._running = false;
      this._error = this.localize.term("searchManager_searchParseError", describeError(parsed.error, parsed.response));
      return;
    }

    const filters = parsed.data.filters;
    const matched = await DiagnoseService.matchNode({
      path: { target, key },
      body: { filters, culture: this.#culture ?? null },
      signal: controller.signal,
    }).catch(failed);
    if (controller.signal.aborted) return;
    this._running = false;
    if (matched.data) {
      this._filters = filters;
      this._result = matched.data;
    } else {
      // A 400 carries the engine's reason (it cannot test queries) as ProblemDetails.
      this._result = undefined;
      this._error = this.localize.term("searchManager_matchError", describeError(matched.error, matched.response));
    }
  }

  /** Runs the typed query and writes it to the URL. */
  #submit(): void {
    const input = this._input.trim();
    this.dispatchEvent(new SearchManagerDiagnoseChangeEvent({ q: input || undefined }));
    void this.#run(input);
  }

  /** Runs the last query again after the index or culture changed. */
  #rerun(): void {
    if (this._result || this._error) void this.#run(this._input.trim());
  }

  /**
   * Renders the pickers, the query box and the result.
   *
   * @returns The template.
   */
  override render() {
    const present = matchableIndexes(this.indexes);
    if (present.length === 0) {
      return html`<p class="sub">${this.localize.term("searchManager_matchNoIndex")}</p>`;
    }
    const target = this.#targetName;
    const culture = this.#culture;
    const indexOptions: Array<SelectOption> = present.map((index) => ({
      name: index.target,
      value: index.target,
      selected: index.target === target,
    }));
    const cultureOptions: Array<SelectOption> = this.cultures.map((option) => ({
      name: option.iso,
      value: option.iso,
      selected: option.iso === culture,
    }));

    return html`
      <div class="pickers">
        <div class="picker">
          <uui-label for="match-index">${this.localize.term("searchManager_exploreIndex")}</uui-label>
          <uui-select
            id="match-index"
            label=${this.localize.term("searchManager_exploreIndex")}
            .options=${indexOptions}
            @change=${(event: Event) => {
              this._target = (event.target as HTMLSelectElement).value;
              this.#rerun();
            }}
          ></uui-select>
        </div>
        ${
          cultureOptions.length > 0
            ? html`<div class="picker">
                <uui-label for="match-culture">${this.localize.term("searchManager_exploreCulture")}</uui-label>
                <uui-select
                  id="match-culture"
                  label=${this.localize.term("searchManager_exploreCulture")}
                  .options=${cultureOptions}
                  @change=${(event: Event) => {
                    this._culture = (event.target as HTMLSelectElement).value;
                    this.dispatchEvent(new SearchManagerDiagnoseChangeEvent({ culture: this._culture }));
                    this.#rerun();
                  }}
                ></uui-select>
              </div>`
            : nothing
        }
      </div>
      <uui-input
        class="query"
        label=${this.localize.term("searchManager_matchQueryLabel")}
        placeholder=${this.localize.term("searchManager_matchQueryPlaceholder")}
        .value=${this._input}
        @input=${(event: Event) => (this._input = String((event.target as HTMLInputElement).value))}
        @keydown=${(event: KeyboardEvent) => {
          if (event.key === "Enter") this.#submit();
        }}
      >
        <uui-button
          slot="append"
          look="primary"
          label=${this.localize.term("searchManager_matchRun")}
          ?disabled=${this._running}
          @click=${() => this.#submit()}
        ></uui-button>
      </uui-input>
      ${this._running ? html`<uui-loader-bar></uui-loader-bar>` : nothing}
      ${this._error ? html`<p class="error" role="alert">${this._error}</p>` : nothing}
      ${this._result ? this.#renderResult(this._result) : nothing}
    `;
  }

  /**
   * Renders the verdict and the chips' outcomes.
   *
   * @param result - The test's answer.
   * @returns The template.
   */
  #renderResult(result: NodeMatchResult) {
    const verdict = verdictLook(result);
    const term = (key: string, ...args: Array<string>) => this.localize.term(key, ...args);
    return html`
      <div class="verdict" role="status">
        <umb-icon name=${verdict.icon} style=${`color: ${verdict.color}`}></umb-icon>
        <strong>${this.localize.term(verdict.label)}</strong>
      </div>
      ${
        result.inIndex && this._filters.length > 0
          ? html`<ul class="clauses">
                ${pairClauses(this._filters, result).map(({ filter, clause }, index) => {
                  const look = clauseLook(clauseOutcome(clause));
                  return html`<li data-clause=${index}>
                    <umb-icon name=${look.icon} style=${`color: ${look.color}`}></umb-icon>
                    <div>
                      <div>
                        <span class="chip">${describeFilter(filter, undefined, term).label}</span>
                        <span>${this.localize.term(look.label)}</span>
                      </div>
                      ${clause?.skippedReason ? html`<div class="sub">${clause.skippedReason}</div>` : nothing}
                      ${
                        // Free-text chips search every field, so there is no single value to show.
                        clause?.field
                          ? html`<div class="sub">
                              ${this.localize.term("searchManager_matchNodeValues", clause.field)}
                              <span class="mono"
                                >${
                                  clause.nodeValues.length > 0
                                    ? clause.nodeValues.join(", ")
                                    : this.localize.term("searchManager_matchNoValue")
                                }</span
                              >
                            </div>`
                          : nothing
                      }
                    </div>
                  </li>`;
                })}
              </ul>
              <p class="sub">${this.localize.term("searchManager_matchOrNote")}</p>`
          : nothing
      }
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-4);
      }

      .pickers {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-5);
      }

      .picker {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .query {
        width: 100%;
      }

      /* The verdict stays in view while the chips below it scroll. */
      .verdict {
        position: sticky;
        top: 0;
        z-index: 1;
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-2) 0;
        background: var(--uui-color-surface);
      }

      .clauses {
        list-style: none;
        margin: 0;
        padding: 0;
        display: grid;
        gap: var(--uui-size-space-3);
      }

      .clauses li {
        display: flex;
        gap: var(--uui-size-space-2);
        align-items: flex-start;
      }

      .chip {
        font-weight: bold;
        margin-right: var(--uui-size-space-2);
      }

      .sub {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
        margin: 0;
      }

      .mono {
        font-family: monospace;
        overflow-wrap: anywhere;
      }

      .error {
        color: var(--uui-color-danger-standalone);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-node-match": SearchManagerNodeMatchElement;
  }
}
