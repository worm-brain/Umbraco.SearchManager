import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UMB_MANAGEMENT_API_SERVER_EVENT_CONTEXT } from "@umbraco-cms/backoffice/management-api";
import { umbOpenModal } from "@umbraco-cms/backoffice/modal";
import { UMB_NOTIFICATION_CONTEXT } from "@umbraco-cms/backoffice/notification";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { IndexesService, RebuildsService, ServerService } from "../api/sdk.gen.js";
import type { IndexSummary, RebuildHistoryEntry, RebuildRun, ServerInfo } from "../api/types.gen.js";
import { SEARCH_MANAGER_INDEX_DETAILS_MODAL } from "../details/index-details-modal.token.js";
import { indexFromQuery } from "../umbraco-search/search-manager-links.js";
import { agoParts, latestEntryByIndex } from "../rebuilds/history-model.js";
import { SEARCH_MANAGER_REBUILD_CONFIRM_MODAL } from "../rebuilds/rebuild-confirm-modal.token.js";
import { REBUILD_EVENT_SOURCES, REBUILD_EVENT_TYPES } from "../rebuilds/rebuild-events.js";
import {
  estimatePercent,
  formatDuration,
  indexKey,
  isActive,
  latestRunByIndex,
  rebuildBlockReason,
  toggleAll,
} from "../rebuilds/rebuild-model.js";
import { RebuildsPoller } from "../rebuilds/rebuilds-poller.js";
import "../rebuilds/rebuilds-panel.element.js";
import { describeError } from "../shared/describe-error.js";
import { DATE_TIME } from "../shared/backoffice-floor.js";
import { engineOptions, filterByEngine, type EngineFilter } from "./engine-filter.js";
import { engineLabel, formatBytes, formatCount, healthLook } from "./index-format.js";

/**
 * The Indexes view: every index the user may see, from every source, with its health and the
 * counts the core Examine dashboard only shows one index at a time, plus bulk rebuilds.
 *
 * Loads `GET /indexes` when connected and on "Reload". Rows are a `uui-table`; health is an icon
 * plus a text label (and the source's message for an unhealthy index), so colour is never the
 * only signal. Counts a source cannot give show as a dash.
 *
 * Rebuilds: rows have select boxes (disabled, with the reason, for an index that cannot rebuild
 * or is already queued), "Rebuild N indexes", "Rebuild all" and a per-row rebuild button. Each
 * opens the confirmation modal, then `POST /rebuilds` queues the runs on the server. The Rebuilds
 * panel shows the server's runs. While Umbraco's server event hub is connected, each rebuild event
 * re-reads `GET /rebuilds`; when it is not, the runs are polled while any is queued or running.
 * The index list reloads when a run finishes so counts are fresh. Rows keep the server's order,
 * so an update never moves a row.
 *
 * Server: the toolbar names the server the user is on (`GET /server`); on a load-balanced site a
 * banner, and a line in the confirmation, say core Examine rebuilds only affect this server.
 *
 * A link with `?index=<name>` (from the Search Manager box on Umbraco Search's index page) opens
 * that index's details sidebar once the list has loaded.
 *
 * History: "Last rebuilt" comes from `GET /rebuilds/history/latest` (reloaded with the indexes);
 * the name and the details button open the details sidebar (history and provider properties).
 *
 * Bound by the `Umbraco.Community.SearchManager.WorkspaceView.Indexes` manifest.
 *
 * @element search-manager-indexes-view
 */
@customElement("search-manager-indexes-view")
export class SearchManagerIndexesViewElement extends UmbLitElement {
  /** The indexes from the last successful load. */
  @state()
  private _indexes: IndexSummary[] = [];

  /** Whether a load is in flight. */
  @state()
  private _loading = false;

  /** A message for the last failed load, shown as text. */
  @state()
  private _error?: string;

  /** The engine filter: every engine, or one engine id. */
  @state()
  private _engine: EngineFilter = "all";

  /** The server's rebuild runs, oldest first. */
  @state()
  private _runs: RebuildRun[] = [];

  /** Each index's latest recorded rebuild, keyed by index. */
  @state()
  private _latest = new Map<string, RebuildHistoryEntry>();

  /** The server the user is on; undefined until loaded or when it cannot be read. */
  @state()
  private _server?: ServerInfo;

  /** Names of the selected indexes. */
  @state()
  private _selected = new Set<string>();

  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;
  #detailsRequestHandled = false;
  #poller = new RebuildsPoller(
    () => RebuildsService.getRebuilds(),
    (runs) => this.#onRuns(runs),
  );

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => (this.#notifications = context));

    // Live status: poll only while the server event hub is not connected, and re-read the runs
    // on each rebuild event (the server routes them only to users allowed to see them).
    this.consumeContext(UMB_MANAGEMENT_API_SERVER_EVENT_CONTEXT, (context) => {
      if (!context) return;
      this.observe(
        context.isConnected,
        (connected) => void this.#poller.setPolling(connected !== true),
        "searchManagerRebuildEventsConnected",
      );
      this.observe(
        context.byEventSourcesAndEventTypes(REBUILD_EVENT_SOURCES, REBUILD_EVENT_TYPES),
        () => void this.#poller.refresh(),
        "searchManagerRebuildEvents",
      );
    });
  }

  /** Loads the indexes and the rebuild runs as soon as the view is in the page. */
  override connectedCallback(): void {
    super.connectedCallback();
    void this.#load();
    void this.#poller.refresh();
    void ServerService.getServer().then(({ data }) => (this._server = data));
  }

  /** Stops polling when the view leaves the page (the runs carry on on the server). */
  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#poller.stop();
  }

  /**
   * Fetches the index list (keeping the previous rows while it loads) and each index's latest
   * recorded rebuild. A history that cannot be read leaves "Last rebuilt" as it was.
   */
  async #load(): Promise<void> {
    this._loading = true;
    const [{ data, error, response }, latest] = await Promise.all([
      IndexesService.getIndexes(),
      RebuildsService.getLatestRebuilds(),
    ]);
    this._loading = false;
    if (latest.data) this._latest = latestEntryByIndex(latest.data);
    if (data) {
      this._indexes = data;
      this._error = undefined;
      this.#openRequestedDetails();
    } else {
      this._error = describeError(error, response);
    }
  }

  /**
   * Takes a new list of runs; reloads the indexes when a run that was active has finished, so
   * health and counts reflect the rebuild.
   *
   * @param runs - The runs, oldest first.
   */
  #onRuns(runs: RebuildRun[]): void {
    const wasActive = new Set(this._runs.filter((run) => isActive(run)).map((run) => run.id));
    const finished = runs.some((run) => wasActive.has(run.id) && !isActive(run));
    this._runs = runs;
    if (finished) void this.#load();
  }

  /**
   * Confirms and queues a rebuild of the named indexes, reporting any the server refused.
   *
   * @param names - The indexes to rebuild.
   */
  async #rebuild(names: string[]): Promise<void> {
    const indexes = this._indexes.filter((index) => names.includes(index.name));
    if (indexes.length === 0) return;

    let mode: "OneAtATime" | "AllAtOnce";
    try {
      ({ mode } = await umbOpenModal(this, SEARCH_MANAGER_REBUILD_CONFIRM_MODAL, {
        data: {
          indexes: indexes.map((index) => ({
            name: index.name,
            zeroDowntime: index.zeroDowntime,
            engine: index.engine,
          })),
          loadBalancedServer: this._server?.isLoadBalanced ? this._server.machineName : undefined,
        },
      }));
    } catch {
      return; // Cancelled.
    }

    const { data, error, response } = await RebuildsService.queueRebuilds({
      body: { indexNames: indexes.map((index) => index.name), mode },
    });
    if (!data) {
      this.#notifications?.peek("danger", {
        data: { message: this.localize.term("searchManager_rebuildError", describeError(error, response)) },
      });
      return;
    }

    for (const refusal of data.refused) {
      this.#notifications?.peek("warning", {
        data: { message: this.localize.term("searchManager_rebuildRefused", refusal.indexName, refusal.reason) },
      });
    }

    this._selected = new Set();
    await this.#poller.refresh();
  }

  /**
   * Cancels a queued run.
   *
   * @param id - The run id.
   */
  async #cancel(id: string): Promise<void> {
    const { error, response } = await RebuildsService.cancelRebuild({ path: { id } });
    if (error) {
      this.#notifications?.peek("warning", { data: { message: describeError(error, response) } });
    }
    await this.#poller.refresh();
  }

  /** Clears finished runs from the panel. */
  async #clearFinished(): Promise<void> {
    await RebuildsService.clearFinishedRebuilds();
    await this.#poller.refresh();
  }

  /**
   * Renders the Rebuilds panel (while there are runs), the toolbar and the table.
   *
   * @returns The template.
   */
  override render() {
    const latest = latestRunByIndex(this._runs);
    const busy = this.localize.term("searchManager_rebuildBusy");
    const rebuildable = this._indexes.filter(
      (index) => !rebuildBlockReason(index, latest.get(indexKey(index.name)), busy),
    );
    const selected = rebuildable.filter((index) => this._selected.has(index.name)).map((index) => index.name);

    return html`
      ${this.#renderLoadBalancedBanner()}
      ${
        this._runs.length > 0
          ? html`<search-manager-rebuilds-panel
              .runs=${this._runs}
              @cancel-run=${(event: CustomEvent<string>) => void this.#cancel(event.detail)}
              @clear-finished=${() => void this.#clearFinished()}
            ></search-manager-rebuilds-panel>`
          : nothing
      }
      <uui-box>
        <div slot="header" class="toolbar">
          <span>${this.localize.term("searchManager_indexesCount", this._indexes.length)}</span>
          ${this.#renderEngineFilter()}
          <span class="spacer"></span>
          ${
            this._server
              ? html`<span class="sub server">
                  ${this.localize.term(
                    "searchManager_serverLine",
                    this._server.machineName,
                    this.localize.term(`searchManager_serverRole${this._server.role}`),
                  )}
                </span>`
              : nothing
          }
          <uui-button
            look="outline"
            label=${this.localize.term("searchManager_rebuildAll")}
            ?disabled=${rebuildable.length === 0}
            @click=${() => void this.#rebuild(rebuildable.map((index) => index.name))}
          ></uui-button>
          <uui-button
            look="secondary"
            label=${this.localize.term("searchManager_reload")}
            .state=${this._loading ? "waiting" : undefined}
            @click=${() => void this.#load()}
          ></uui-button>
        </div>
        ${
          selected.length > 0
            ? html`<div class="selection">
                <strong>${this.localize.term("searchManager_selectedCount", selected.length)}</strong>
                <uui-button
                  look="primary"
                  color="danger"
                  label=${this.localize.term("searchManager_rebuildSelected", selected.length)}
                  @click=${() => void this.#rebuild(selected)}
                ></uui-button>
                <uui-button
                  look="placeholder"
                  label=${this.localize.term("searchManager_clearSelection")}
                  @click=${() => (this._selected = new Set())}
                ></uui-button>
              </div>`
            : nothing
        }
        ${
          this._error
            ? html`<p class="error" role="alert">${this.localize.term("searchManager_indexesError", this._error)}</p>`
            : nothing
        }
        ${this.#renderTable(latest, busy)}
      </uui-box>
    `;
  }

  /**
   * Renders the engine filter (All, Examine, Umbraco Search, ...), with each button's count. It is
   * left out when every index comes from one engine, since it could only ever show "all".
   *
   * @returns The template, or nothing.
   */
  #renderEngineFilter() {
    const options = engineOptions(this._indexes);
    if (options.length <= 2) return nothing;
    return html`
      <uui-button-group role="group" aria-label=${this.localize.term("searchManager_engineFilterLabel")}>
        ${options.map(
          (option) => html`
            <uui-button
              look=${this._engine === option.value ? "primary" : "secondary"}
              aria-pressed=${this._engine === option.value ? "true" : "false"}
              label=${
                option.value === "all"
                  ? this.localize.term("searchManager_engineAll", option.count)
                  : this.localize.term("searchManager_engineOption", engineLabel(option.value), option.count)
              }
              @click=${() => (this._engine = option.value)}
            ></uui-button>
          `,
        )}
      </uui-button-group>
    `;
  }

  /**
   * Renders the index table, or a loader before the first answer arrives.
   *
   * @param latest - Each index's latest run.
   * @param busy - The reason text for an index already queued or running.
   * @returns The template.
   */
  #renderTable(latest: Map<string, RebuildRun>, busy: string) {
    if (this._loading && this._indexes.length === 0) return html`<uui-loader-bar></uui-loader-bar>`;
    if (this._indexes.length === 0) return nothing;

    const visible = filterByEngine(this._indexes, this._engine);
    const eligible = visible
      .filter((index) => !rebuildBlockReason(index, latest.get(indexKey(index.name)), busy))
      .map((index) => index.name);
    const allChecked = eligible.length > 0 && eligible.every((name) => this._selected.has(name));

    return html`
      <uui-table aria-label=${this.localize.term("searchManager_indexesTableLabel")}>
        <uui-table-head>
          <uui-table-head-cell class="select">
            <uui-checkbox
              aria-label=${this.localize.term("searchManager_selectAll")}
              ?checked=${allChecked}
              ?disabled=${eligible.length === 0}
              @change=${() => (this._selected = toggleAll(this._selected, eligible))}
            ></uui-checkbox>
          </uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term("searchManager_columnIndex")}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term("searchManager_columnHealth")}</uui-table-head-cell>
          <uui-table-head-cell class="number"
            >${this.localize.term("searchManager_columnDocuments")}</uui-table-head-cell
          >
          <uui-table-head-cell class="number">${this.localize.term("searchManager_columnDeleted")}</uui-table-head-cell>
          <uui-table-head-cell class="number">${this.localize.term("searchManager_columnFields")}</uui-table-head-cell>
          <uui-table-head-cell class="number">${this.localize.term("searchManager_columnSize")}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term("searchManager_columnLastRebuilt")}</uui-table-head-cell>
          <uui-table-head-cell class="actions"
            >${this.localize.term("searchManager_columnActions")}</uui-table-head-cell
          >
        </uui-table-head>
        ${visible.map((index) => this.#renderRow(index, latest.get(indexKey(index.name)), busy))}
      </uui-table>
    `;
  }

  /**
   * Renders one index row.
   *
   * @param index - The index.
   * @param run - Its latest rebuild run, if any.
   * @param busy - The reason text for an index already queued or running.
   * @returns The row template.
   */
  #renderRow(index: IndexSummary, run: RebuildRun | undefined, busy: string) {
    const blocked = rebuildBlockReason(index, run, busy);
    const blockedTitle = blocked ? this.localize.term("searchManager_rebuildUnavailable", blocked) : undefined;
    const { health, sub } = this.#health(index, run);
    const look = healthLook(health);
    return html`
      <uui-table-row>
        <uui-table-cell class="select">
          <uui-checkbox
            aria-label=${this.localize.term("searchManager_selectIndex", index.name)}
            title=${blockedTitle ?? ""}
            ?checked=${!blocked && this._selected.has(index.name)}
            ?disabled=${!!blocked}
            @change=${() => this.#toggle(index.name)}
          ></uui-checkbox>
        </uui-table-cell>
        <uui-table-cell>
          <div class="name">
            <button
              type="button"
              class="open"
              title=${this.localize.term("searchManager_detailsOpen", index.name)}
              @click=${() => this.#openDetails(index)}
            >
              <strong>${index.name}</strong>
            </button>
            <uui-tag look="secondary" color=${index.engine === "examine" ? "default" : "positive"}
              >${engineLabel(index.engine)}</uui-tag
            >
            ${
              index.zeroDowntime
                ? html`<uui-tag look="outline">${this.localize.term("searchManager_zeroDowntime")}</uui-tag>`
                : nothing
            }
          </div>
          ${index.description ? html`<div class="sub">${index.description}</div>` : nothing}
        </uui-table-cell>
        <uui-table-cell>
          <div class="health">
            <umb-icon name=${look.icon} style=${`color: ${look.color}`}></umb-icon>
            <div>
              <div>${this.localize.term(`searchManager_health${health}`)}</div>
              ${sub ? html`<div class="sub">${sub}</div>` : nothing}
            </div>
          </div>
        </uui-table-cell>
        <uui-table-cell class="number">${formatCount(index.documentCount)}</uui-table-cell>
        <uui-table-cell class="number">${formatCount(index.deletedDocumentCount)}</uui-table-cell>
        <uui-table-cell class="number">${formatCount(index.fieldCount)}</uui-table-cell>
        <uui-table-cell class="number">${formatBytes(index.sizeInBytes)}</uui-table-cell>
        <uui-table-cell>${this.#renderLastRebuilt(this._latest.get(indexKey(index.name)))}</uui-table-cell>
        <uui-table-cell class="actions">
          <uui-button
            look="secondary"
            compact
            label=${this.localize.term("searchManager_rebuildIndex", index.name)}
            title=${blockedTitle ?? this.localize.term("searchManager_rebuildIndex", index.name)}
            ?disabled=${!!blocked}
            @click=${() => void this.#rebuild([index.name])}
            ><umb-icon name="icon-sync"></umb-icon
          ></uui-button>
          <uui-button
            look="secondary"
            compact
            label=${this.localize.term("searchManager_detailsOpen", index.name)}
            title=${this.localize.term("searchManager_detailsOpen", index.name)}
            @click=${() => this.#openDetails(index)}
            ><umb-icon name="icon-info"></umb-icon
          ></uui-button>
        </uui-table-cell>
      </uui-table-row>
    `;
  }

  /**
   * The health a row shows: a queued or running rebuild (that clears the index) overrides the
   * index's own health; otherwise the source's health and message.
   *
   * @param index - The index.
   * @param run - Its latest run, if any.
   * @returns The health and the line under it.
   */
  #health(index: IndexSummary, run: RebuildRun | undefined): { health: IndexSummary["health"]; sub?: string } {
    if (run?.status === "Queued") {
      return { health: index.health, sub: this.localize.term("searchManager_rebuildQueuedHealth") };
    }
    if (run?.status === "Running" && !run.zeroDowntime) {
      return {
        health: "Rebuilding",
        sub: this.#estimateText(run),
      };
    }
    return { health: index.health, sub: index.healthMessage ?? undefined };
  }

  /**
   * The estimate line under a rebuilding index's health, or nothing before there is an estimate.
   *
   * @param run - The running run.
   * @returns The text, or undefined.
   */
  #estimateText(run: RebuildRun): string | undefined {
    const percent = estimatePercent(run);
    return percent === null ? undefined : this.localize.term("searchManager_rebuildRunningHealth", percent);
  }

  /**
   * Renders the "Last rebuilt" cell: how long ago, then the duration and outcome.
   *
   * @param entry - The index's latest recorded rebuild, if any.
   * @returns The template.
   */
  #renderLastRebuilt(entry: RebuildHistoryEntry | undefined) {
    if (!entry) return html`<span class="sub">${this.localize.term("searchManager_lastRebuiltNone")}</span>`;
    const { value, unit } = agoParts(entry.endedAt);
    const duration = formatDuration(entry.startedAt, entry.endedAt);
    const failed = entry.outcome === "Failed";
    return html`<div class="last-rebuilt">
      <div title=${this.localize.date(entry.endedAt, DATE_TIME)}>
        ${
          failed
            ? html`<umb-icon name="icon-alert" style="color: var(--uui-color-danger-standalone)"></umb-icon>`
            : nothing
        }
        ${this.localize.relativeTime(value, unit, { numeric: "auto" })}
      </div>
      <div class=${failed ? "sub error" : "sub"}>
        ${this.localize.term(failed ? "searchManager_lastRebuiltFailed" : "searchManager_lastRebuiltSucceeded", duration)}
      </div>
    </div>`;
  }

  /**
   * Renders the load-balancing banner, on a site with more than one server.
   *
   * @returns The template, or nothing.
   */
  #renderLoadBalancedBanner() {
    if (!this._server?.isLoadBalanced) return nothing;
    return html`
      <div class="banner" role="note">
        <umb-icon name="icon-alert"></umb-icon>
        <span>
          ${this.localize.term(
            "searchManager_loadBalancedBanner",
            this._server.machineName,
            this._server.activeServers,
          )}
        </span>
      </div>
    `;
  }

  /**
   * Opens the details sidebar for the index named in the URL (`?index=`, as links from Umbraco
   * Search's index page carry), once, after the first load.
   */
  #openRequestedDetails(): void {
    if (this.#detailsRequestHandled) return;
    this.#detailsRequestHandled = true;
    const name = indexFromQuery(window.location.search);
    const index = name && this._indexes.find((candidate) => indexKey(candidate.name) === indexKey(name));
    if (index) this.#openDetails(index);
  }

  /**
   * Opens the details sidebar for an index.
   *
   * @param index - The index.
   */
  #openDetails(index: IndexSummary): void {
    void umbOpenModal(this, SEARCH_MANAGER_INDEX_DETAILS_MODAL, { data: { index } }).catch(() => undefined);
  }

  /**
   * Selects or deselects one index.
   *
   * @param name - The index name.
   */
  #toggle(name: string): void {
    const next = new Set(this._selected);
    if (next.has(name)) next.delete(name);
    else next.add(name);
    this._selected = next;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-layout-1);
        padding: var(--uui-size-layout-1);
      }

      .toolbar {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
        width: 100%;
      }

      .spacer {
        flex: 1;
      }

      .banner {
        display: flex;
        gap: var(--uui-size-space-3);
        align-items: flex-start;
        color: var(--uui-color-warning-contrast);
        background: var(--uui-color-warning);
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
        border-radius: var(--uui-border-radius);
      }

      .selection {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
        margin-bottom: var(--uui-size-space-4);
      }

      .name {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .health {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-2);
      }

      .sub {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
        overflow-wrap: anywhere;
      }

      /* "9 minutes ago" over "0 s, succeeded": two short lines, not four wrapped words. */
      .last-rebuilt div {
        white-space: nowrap;
      }

      .select {
        width: 44px;
      }

      /* The index name opens the details sidebar; it looks like the name, not a button. */
      .open {
        all: unset;
        cursor: pointer;
      }

      .open:hover strong,
      .open:focus-visible strong {
        text-decoration: underline;
      }

      .open:focus-visible {
        outline: 2px solid var(--uui-color-focus);
        outline-offset: 2px;
      }

      .number {
        text-align: right;
        font-variant-numeric: tabular-nums;
        white-space: nowrap;
      }

      /* Row buttons sit side by side. */
      .actions {
        text-align: right;
        white-space: nowrap;
      }

      .error {
        color: var(--uui-color-danger);
      }
    `,
  ];
}

export { SearchManagerIndexesViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-indexes-view": SearchManagerIndexesViewElement;
  }
}
