import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbModalBaseElement } from "@umbraco-cms/backoffice/modal";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { RebuildsService } from "../api/sdk.gen.js";
import type { RebuildHistoryEntry } from "../api/types.gen.js";
import { engineLabel, formatCount } from "../indexes/index-format.js";
import { formatDuration } from "../rebuilds/rebuild-model.js";
import { DATE_TIME } from "../shared/backoffice-floor.js";
import { describeError } from "../shared/describe-error.js";
import { fieldStatisticsUrl } from "../shared/explore-links.js";
import type { IndexDetailsModalData } from "./index-details-modal.token.js";

/**
 * The index details sidebar: the index's persisted rebuild history (last 20 runs, newest first,
 * from `GET /rebuilds/history`) and its provider properties, as the core Examine dashboard shows
 * them, plus a link to the index's field statistics in the Diagnose view when the index can give
 * them (`capabilities.termStatistics`).
 *
 * Bound by the `Umbraco.Community.SearchManager.Modal.IndexDetails` manifest; opened with
 * `SEARCH_MANAGER_INDEX_DETAILS_MODAL`.
 *
 * @element search-manager-index-details-modal
 */
@customElement("search-manager-index-details-modal")
export class SearchManagerIndexDetailsModalElement extends UmbModalBaseElement<IndexDetailsModalData, never> {
  /** The recorded runs, newest first; undefined while loading. */
  @state()
  private _runs?: RebuildHistoryEntry[];

  /** Why the history could not be loaded, if it could not. */
  @state()
  private _error?: string;

  /** Loads the history once the sidebar is open. */
  override connectedCallback(): void {
    super.connectedCallback();
    void this.#load();
  }

  /** Fetches the index's history. */
  async #load(): Promise<void> {
    const name = this.data?.index.name;
    if (!name) return;
    const { data, error, response } = await RebuildsService.getRebuildHistory({ query: { indexName: name } });
    if (data) this._runs = data;
    else this._error = describeError(error, response);
  }

  /**
   * Renders the sidebar.
   *
   * @returns The template.
   */
  override render() {
    const index = this.data?.index;
    if (!index) return nothing;
    return html`
      <umb-body-layout headline=${index.name}>
        <p class="sub">${[engineLabel(index.engine), index.description].filter(Boolean).join(" · ")}</p>
        <uui-box headline=${this.localize.term("searchManager_detailsHistoryTitle")}>
          <span slot="header-actions" class="sub">${this.localize.term("searchManager_detailsHistoryHint")}</span>
          ${this.#renderHistory()}
        </uui-box>
        <uui-box headline=${this.localize.term("searchManager_detailsPropertiesTitle")}>
          ${this.#renderProperties(index.properties)}
        </uui-box>
        ${
          index.capabilities.termStatistics
            ? html`<uui-button
                slot="actions"
                look="secondary"
                href=${fieldStatisticsUrl(index.name)}
                label=${this.localize.term("searchManager_boxFieldStatistics")}
                @click=${
                  // The link navigates within the same workspace; close the sidebar so it does not
                  // stay open over the Diagnose view. The anchor's navigation still happens.
                  () => this._rejectModal()
                }
              ></uui-button>`
            : nothing
        }
        <uui-button
          slot="actions"
          look="secondary"
          label=${this.localize.term("general_close")}
          @click=${() => this._rejectModal()}
        ></uui-button>
      </umb-body-layout>
    `;
  }

  /**
   * Renders the recorded runs with the backoffice's history list.
   *
   * @returns The template.
   */
  #renderHistory() {
    if (this._error) {
      return html`<p class="error" role="alert">
        ${this.localize.term("searchManager_detailsLoadError", this._error)}
      </p>`;
    }
    if (!this._runs) return html`<uui-loader-bar></uui-loader-bar>`;
    if (this._runs.length === 0) return html`<p class="sub">${this.localize.term("searchManager_detailsNoRuns")}</p>`;

    return html` <umb-history-list> ${this._runs.map((run) => this.#renderRun(run))} </umb-history-list> `;
  }

  /**
   * Renders one recorded run: who started it and when, the outcome, duration, document counts and
   * server, and the error of a failed run.
   *
   * @param run - The run.
   * @returns The template.
   */
  #renderRun(run: RebuildHistoryEntry) {
    const failed = run.outcome === "Failed";
    const who =
      run.trigger === "User" && run.triggeredBy
        ? this.localize.term("searchManager_historyTriggerUser", run.triggeredBy)
        : this.localize.term("searchManager_historyTriggerOther");
    return html`
      <umb-history-item name=${who} detail=${this.localize.date(run.startedAt, DATE_TIME)}>
        <umb-icon
          slot="avatar"
          name=${failed ? "icon-alert" : "icon-check"}
          style=${`color: var(${failed ? "--uui-color-danger-standalone" : "--uui-color-positive-standalone"})`}
        ></umb-icon>
        <div class="run">
          <uui-tag look="secondary" color=${failed ? "danger" : "positive"}>
            ${this.localize.term(failed ? "searchManager_historyFailed" : "searchManager_historySucceeded")}
          </uui-tag>
          <span>
            ${
              // Rebuilds started outside Search Manager carry no document counts.
              run.documentsBefore == null && run.documentsAfter == null
                ? this.localize.term(
                    "searchManager_historyDetailNoCounts",
                    formatDuration(run.startedAt, run.endedAt),
                    run.server,
                  )
                : this.localize.term(
                    "searchManager_historyDetail",
                    formatDuration(run.startedAt, run.endedAt),
                    formatCount(run.documentsBefore),
                    formatCount(run.documentsAfter),
                    run.server,
                  )
            }
          </span>
        </div>
        ${failed && run.error ? html`<code class="error-text">${run.error}</code>` : nothing}
      </umb-history-item>
    `;
  }

  /**
   * Renders the provider properties as a key/value list.
   *
   * @param properties - The properties.
   * @returns The template.
   */
  #renderProperties(properties: Record<string, string>) {
    const entries = Object.entries(properties);
    if (entries.length === 0)
      return html`<p class="sub">${this.localize.term("searchManager_detailsNoProperties")}</p>`;
    return html`
      <dl>
        ${entries.map(
          ([key, value]) =>
            html`<dt>${key}</dt>
              <dd>${value}</dd>`,
        )}
      </dl>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      uui-box + uui-box {
        margin-top: var(--uui-size-layout-1);
      }

      .sub {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      p.sub {
        margin-top: 0;
      }

      .run {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .error,
      .error-text {
        color: var(--uui-color-danger-standalone);
      }

      .error-text {
        display: block;
        margin-top: var(--uui-size-space-2);
        overflow-wrap: anywhere;
        font-size: var(--uui-type-small-size);
      }

      dl {
        display: grid;
        grid-template-columns: minmax(120px, max-content) 1fr;
        gap: var(--uui-size-space-2) var(--uui-size-space-5);
        margin: 0;
      }

      dt {
        font-weight: bold;
      }

      dd {
        margin: 0;
        overflow-wrap: anywhere;
      }
    `,
  ];
}

export { SearchManagerIndexDetailsModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-index-details-modal": SearchManagerIndexDetailsModalElement;
  }
}
