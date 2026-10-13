import { css, customElement, html, nothing, state, type TemplateResult } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { ExploreService, type ExploreTarget } from "../api/index.js";
import type { SelectOption } from "../shared/backoffice-floor.js";
import { describeError } from "../shared/describe-error.js";
import "../shared/empty-state.element.js";
import { SearchManagerDiagnoseChangeEvent } from "./diagnose-events.js";
import {
  createDiagnoseState,
  decodeDiagnoseState,
  encodeDiagnoseState,
  mergeDiagnoseIntoSearch,
  type DiagnoseTool,
  type DiagnoseViewState,
} from "./diagnose-state.js";
import "./fields/field-statistics.element.js";

/** The index Diagnose opens on when the URL names none, as Explore does. */
const DEFAULT_INDEX = "ExternalIndex";

/** One tool of the switcher. */
interface DiagnoseToolDefinition {
  /** The tool's key in the URL. */
  tool: DiagnoseTool;
  /** The lang key of its switcher label. */
  label: string;
  /** Whether the tool works on one index, so the index picker shows. */
  usesIndex: boolean;
  /**
   * Renders the tool.
   *
   * @param target - The picked index; undefined for a tool that does not use one.
   * @returns The template.
   */
  render: (target: ExploreTarget | undefined) => TemplateResult | typeof nothing;
}

/**
 * The Diagnose tab: a tool switcher and, for the tools that work on one index, an index picker,
 * above the tool itself.
 *
 * - **Tools:** a data-driven list ({@link SearchManagerDiagnoseViewElement.tools}); add a tool by
 *   adding an entry. Today: Fields (`search-manager-field-statistics`).
 * - **Index picker:** the index targets of `GET /explore/targets` (searchers are left out; member
 *   indexes are already filtered by the server for users without Members access).
 * - **URL:** the state (tool, index, the tools' own keys) is written to the query string with
 *   `replaceState` and read back when the tab opens, so a link reopens it. `index` stays the key,
 *   so `?index=` links from the Umbraco Search box and the index details open on that index.
 *
 * Bound by the `Umbraco.Community.SearchManager.WorkspaceView.Diagnose` manifest.
 *
 * @element search-manager-diagnose-view
 */
@customElement("search-manager-diagnose-view")
export class SearchManagerDiagnoseViewElement extends UmbLitElement {
  /** The view's state, mirrored in the URL. */
  @state()
  private _state: DiagnoseViewState = createDiagnoseState();

  /** The indexes to pick from; undefined until loaded. */
  @state()
  private _targets?: Array<ExploreTarget>;

  /** Why the indexes could not be loaded, if they could not. */
  @state()
  private _error?: string;

  /** The tools, in switcher order. */
  readonly tools: ReadonlyArray<DiagnoseToolDefinition> = [
    {
      tool: "fields",
      label: "searchManager_diagnoseFields",
      usesIndex: true,
      render: (target) =>
        target
          ? html`<search-manager-field-statistics
              .target=${target}
              ?variants=${this._state.variants}
              .filter=${this._state.filter ?? ""}
            ></search-manager-field-statistics>`
          : nothing,
    },
  ];

  /** Reads the state from the URL and loads the indexes. */
  override connectedCallback(): void {
    super.connectedCallback();
    this._state = decodeDiagnoseState(new URLSearchParams(window.location.search));
    void this.#loadTargets();
  }

  /** The tool shown: the one in the state when it exists, otherwise the first. */
  get #tool(): DiagnoseToolDefinition {
    // The list is never empty, so the first tool always exists.
    return this.tools.find((tool) => tool.tool === this._state.tool) ?? this.tools[0]!;
  }

  /** The index in use: the one in the state when listed, otherwise ExternalIndex, otherwise the first. */
  get #target(): ExploreTarget | undefined {
    const wanted = this._state.index?.toLowerCase();
    return (
      this._targets?.find((target) => target.name.toLowerCase() === wanted) ??
      this._targets?.find((target) => target.name === DEFAULT_INDEX) ??
      this._targets?.[0]
    );
  }

  /** Loads the index targets. */
  async #loadTargets(): Promise<void> {
    const { data, error, response } = await ExploreService.getExploreTargets();
    if (!data) {
      this._error = this.localize.term("searchManager_exploreTargetsError", describeError(error, response));
      return;
    }
    this._targets = data.filter((target) => target.kind === "Index");
  }

  /**
   * Applies a change to the view and writes the URL.
   *
   * @param partial - The keys that changed.
   */
  #update(partial: Partial<DiagnoseViewState>): void {
    this._state = { ...this._state, ...partial };
    // Only while this tab is shown: the other tabs own their own query strings.
    if (!window.location.pathname.includes("/diagnose")) return;
    const search = mergeDiagnoseIntoSearch(window.location.search, encodeDiagnoseState(this._state));
    if (search === window.location.search.replace(/^\?/, "")) return;
    const url = `${window.location.pathname}${search ? `?${search}` : ""}${window.location.hash}`;
    window.history.replaceState(window.history.state, "", url);
  }

  /**
   * Renders the switcher, the index picker and the tool.
   *
   * @returns The template.
   */
  override render() {
    if (this._targets && this._targets.length === 0) {
      return html`<search-manager-empty-state headline=${this.localize.term("searchManager_tabDiagnose")}>
        <p>${this.localize.term("searchManager_diagnoseNoTargets")}</p>
      </search-manager-empty-state>`;
    }

    const tool = this.#tool;
    const target = this.#target;
    return html`
      <div class="bar">${this.#renderSwitcher(tool)} ${tool.usesIndex ? this.#renderIndexPicker(target) : nothing}</div>
      ${this._error ? html`<p class="error" role="alert">${this._error}</p>` : nothing}
      ${!this._targets && !this._error ? html`<uui-loader-bar></uui-loader-bar>` : nothing}
      <div @search-manager-diagnose-change=${(event: SearchManagerDiagnoseChangeEvent) => this.#update(event.change)}>
        ${this._targets ? tool.render(tool.usesIndex ? target : undefined) : nothing}
      </div>
    `;
  }

  /**
   * Renders the tool switcher as a button group, the pressed button marking the tool shown.
   *
   * @param current - The tool shown.
   * @returns The template.
   */
  #renderSwitcher(current: DiagnoseToolDefinition) {
    return html`
      <uui-button-group role="group" aria-label=${this.localize.term("searchManager_diagnoseTool")}>
        ${this.tools.map(
          (tool) => html`
            <uui-button
              look=${tool === current ? "primary" : "secondary"}
              aria-pressed=${tool === current ? "true" : "false"}
              label=${this.localize.term(tool.label)}
              @click=${() => this.#update({ tool: tool.tool })}
            ></uui-button>
          `,
        )}
      </uui-button-group>
    `;
  }

  /**
   * Renders the index picker.
   *
   * @param target - The index in use.
   * @returns The template.
   */
  #renderIndexPicker(target: ExploreTarget | undefined) {
    if (!this._targets) return nothing;
    const options: Array<SelectOption> = this._targets.map((candidate) => ({
      name: candidate.name,
      value: candidate.name,
      selected: candidate.name === target?.name,
    }));
    return html`
      <div class="picker">
        <uui-label for="index">${this.localize.term("searchManager_exploreIndex")}</uui-label>
        <uui-select
          id="index"
          label=${this.localize.term("searchManager_exploreIndex")}
          .options=${options}
          @change=${(event: Event) => this.#update({ index: (event.target as HTMLSelectElement).value })}
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

      .bar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-5);
      }

      .picker {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .error {
        color: var(--uui-color-danger-standalone);
      }
    `,
  ];
}

export { SearchManagerDiagnoseViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-diagnose-view": SearchManagerDiagnoseViewElement;
  }
}
