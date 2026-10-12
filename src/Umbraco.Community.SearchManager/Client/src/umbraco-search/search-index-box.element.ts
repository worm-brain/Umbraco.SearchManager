import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { UMB_WORKSPACE_CONTEXT, type UmbEntityWorkspaceContext } from "@umbraco-cms/backoffice/workspace";
import { IndexesService, RebuildsService } from "../api/sdk.gen.js";
import type { IndexSummary, RebuildHistoryEntry } from "../api/types.gen.js";
import { formatCount } from "../indexes/index-format.js";
import { agoParts } from "../rebuilds/history-model.js";
import { formatDuration } from "../rebuilds/rebuild-model.js";
import { DATE_TIME } from "../shared/backoffice-floor.js";
import { searchManagerViewPath } from "./search-manager-links.js";

/** Umbraco Search's entity type for an index workspace (`UMB_SEARCH_INDEX_ENTITY_TYPE`, 17.2 and 18.2). */
const SEARCH_INDEX_ENTITY_TYPE = "search-index";

/** How many recent runs the box lists under the last one. */
const RECENT_RUNS = 3;

/**
 * The Search Manager box on Umbraco Search's index page: the last rebuild (when, duration,
 * outcome), the three runs before it, the field count, and links into Search Manager's Explore,
 * field statistics (Diagnose) and history for the index.
 *
 * Registered by the Umbraco Search adapter package, server side, as a `searchIndexDetailBox` in
 * the right column, so it exists only where Umbraco Search does. It finds the index from the
 * workspace context of Umbraco Search's index workspace.
 *
 * @element search-manager-search-index-box
 */
@customElement("search-manager-search-index-box")
export class SearchManagerSearchIndexBoxElement extends UmbLitElement {
  /** The index alias the page shows. */
  @state()
  private _alias?: string;

  /** The index, as Search Manager lists it; undefined until loaded or when not listed. */
  @state()
  private _index?: IndexSummary;

  /** The recorded runs, newest first; undefined while loading. */
  @state()
  private _runs?: RebuildHistoryEntry[];

  constructor() {
    super();
    this.consumeContext(UMB_WORKSPACE_CONTEXT, (context) => {
      if (context?.getEntityType() !== SEARCH_INDEX_ENTITY_TYPE) return;
      this.observe(
        (context as unknown as UmbEntityWorkspaceContext).unique,
        (unique) => {
          if (!unique || unique === this._alias) return;
          this._alias = unique;
          void this.#load(unique);
        },
        "searchManagerIndexAlias",
      );
    });
  }

  /**
   * Loads the index's history and its entry in Search Manager's index list.
   *
   * @param alias - The index alias.
   */
  async #load(alias: string): Promise<void> {
    const [history, indexes] = await Promise.all([
      RebuildsService.getRebuildHistory({ query: { indexName: alias } }),
      IndexesService.getIndexes(),
    ]);
    if (alias !== this._alias) return;
    this._runs = history.data ?? [];
    this._index = indexes.data?.find((index) => index.name.toLowerCase() === alias.toLowerCase());
  }

  /**
   * Renders the box.
   *
   * @returns The template.
   */
  override render() {
    if (!this._alias) return nothing;
    const alias = this._alias;
    const [last, ...earlier] = this._runs ?? [];
    return html`
      <uui-box headline=${this.localize.term("searchManager_title")}>
        <dl>
          <dt>${this.localize.term("searchManager_columnLastRebuilt")}</dt>
          <dd>${this._runs ? this.#renderLast(last) : html`<uui-loader-bar></uui-loader-bar>`}</dd>
          ${
            earlier.length > 0
              ? html`<dt>${this.localize.term("searchManager_boxRecentRuns")}</dt>
                  <dd>
                    <ul>
                      ${earlier.slice(0, RECENT_RUNS).map((run) => html`<li>${this.#describe(run)}</li>`)}
                    </ul>
                  </dd>`
              : nothing
          }
          <dt>${this.localize.term("searchManager_columnFields")}</dt>
          <dd>${formatCount(this._index?.fieldCount)}</dd>
        </dl>
        <div class="links">
          <uui-button
            look="secondary"
            compact
            href=${searchManagerViewPath("explore", alias)}
            label=${this.localize.term("searchManager_boxExplore")}
          ></uui-button>
          <uui-button
            look="secondary"
            compact
            href=${searchManagerViewPath("indexes", alias)}
            label=${this.localize.term("searchManager_boxHistory")}
          ></uui-button>
        </div>
      </uui-box>
    `;
  }

  /**
   * Renders the last run: how long ago, then the duration and outcome.
   *
   * @param run - The newest run, if any.
   * @returns The template.
   */
  #renderLast(run: RebuildHistoryEntry | undefined) {
    if (!run) return this.localize.term("searchManager_lastRebuiltNone");
    const { value, unit } = agoParts(run.endedAt);
    return html`
      <div>${this.localize.relativeTime(value, unit, { numeric: "auto" })}</div>
      <div class=${run.outcome === "Failed" ? "sub error" : "sub"}>${this.#outcome(run)}</div>
    `;
  }

  /**
   * One earlier run on a line: when it ran and how it ended.
   *
   * @param run - The run.
   * @returns The text.
   */
  #describe(run: RebuildHistoryEntry): string {
    return `${this.localize.date(run.startedAt, DATE_TIME)}: ${this.#outcome(run)}`;
  }

  /**
   * A run's duration and outcome, as the "Last rebuilt" column words it.
   *
   * @param run - The run.
   * @returns The text.
   */
  #outcome(run: RebuildHistoryEntry): string {
    const duration = formatDuration(run.startedAt, run.endedAt);
    return this.localize.term(
      run.outcome === "Failed" ? "searchManager_lastRebuiltFailed" : "searchManager_lastRebuiltSucceeded",
      duration,
    );
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
      }

      dl {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
        margin: 0 0 var(--uui-size-space-5);
      }

      dt {
        font-weight: bold;
      }

      dd {
        margin: 0 0 var(--uui-size-space-3);
      }

      ul {
        margin: 0;
        padding-left: var(--uui-size-space-5);
      }

      .sub {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      .error {
        color: var(--uui-color-danger-standalone);
      }

      .links {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-3);
      }
    `,
  ];
}

export { SearchManagerSearchIndexBoxElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-search-index-box": SearchManagerSearchIndexBoxElement;
  }
}
