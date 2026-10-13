import { css, customElement, html, nothing, property } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { RebuildRun } from "../api/types.gen.js";
import { estimatePercent, finishedEmpty, formatDuration, waitPosition } from "./rebuild-model.js";

/** How each run status is drawn: an icon and a colour token, always beside text. */
const STATUS_LOOK: Record<RebuildRun["status"], { icon: string; color: string }> = {
  Queued: { icon: "icon-time", color: "var(--uui-color-text-alt)" },
  Running: { icon: "icon-sync", color: "var(--uui-color-warning-standalone)" },
  Succeeded: { icon: "icon-check", color: "var(--uui-color-positive-standalone)" },
  Failed: { icon: "icon-alert", color: "var(--uui-color-danger-standalone)" },
  Cancelled: { icon: "icon-wrong", color: "var(--uui-color-text-alt)" },
};

/**
 * The Rebuilds panel: the server's queue, newest request last, with queued runs' place in line,
 * running runs' estimated progress (labelled as an estimate), and finished runs' outcome. Queued
 * runs can be cancelled; finished ones cleared.
 *
 * Not bound to a manifest; the Indexes view renders it above the table while there are runs.
 *
 * @element search-manager-rebuilds-panel
 * @fires cancel-run - The user cancelled a queued run; `detail` is the run id.
 * @fires clear-finished - The user cleared the finished runs.
 */
@customElement("search-manager-rebuilds-panel")
export class SearchManagerRebuildsPanelElement extends UmbLitElement {
  /** The runs, oldest first, as `GET /rebuilds` returns them. */
  @property({ attribute: false })
  runs: RebuildRun[] = [];

  /**
   * Renders the panel.
   *
   * @returns The template.
   */
  override render() {
    const anyFinished = this.runs.some((run) => run.status !== "Queued" && run.status !== "Running");
    const sequential = this.runs.some((run) => run.mode === "OneAtATime");
    return html`
      <uui-box headline=${this.localize.term("searchManager_rebuildsTitle")} headline-variant="h4">
        <span slot="header" class="sub">
          ${this.localize.term(sequential ? "searchManager_rebuildsOneAtATime" : "searchManager_rebuildsAllAtOnce")}
        </span>
        <uui-button
          slot="header-actions"
          look="secondary"
          compact
          label=${this.localize.term("searchManager_rebuildsClearFinished")}
          ?disabled=${!anyFinished}
          @click=${() => this.dispatchEvent(new CustomEvent("clear-finished", { bubbles: true, composed: true }))}
        ></uui-button>
        <ul>
          ${this.runs.map((run) => this.#renderRun(run))}
        </ul>
      </uui-box>
    `;
  }

  /**
   * Renders one run.
   *
   * @param run - The run.
   * @returns The row template.
   */
  #renderRun(run: RebuildRun) {
    const look = STATUS_LOOK[run.status];
    const percent = estimatePercent(run);
    return html`
      <li>
        <umb-icon name=${look.icon} style=${`color: ${look.color}`}></umb-icon>
        <strong class="name">${run.indexName}</strong>
        <div class="detail">
          ${
            run.status === "Running" && percent !== null
              ? html`<uui-progress-bar
                  progress=${percent}
                  aria-label=${this.localize.term("searchManager_rebuildsProgressLabel", run.indexName)}
                ></uui-progress-bar>`
              : nothing
          }
          <span class=${run.status === "Failed" ? "sub error" : "sub"}>${this.#describe(run, percent)}</span>
        </div>
        ${
          run.status === "Queued"
            ? html`<uui-button
                look="secondary"
                compact
                label=${this.localize.term("searchManager_rebuildsCancel")}
                @click=${() =>
                  this.dispatchEvent(new CustomEvent("cancel-run", { detail: run.id, bubbles: true, composed: true }))}
              ></uui-button>`
            : nothing
        }
      </li>
    `;
  }

  /**
   * The line under a run's name.
   *
   * @param run - The run.
   * @param percent - Its estimated percentage, when running.
   * @returns The text.
   */
  #describe(run: RebuildRun, percent: number | null): string {
    switch (run.status) {
      case "Queued": {
        const position = waitPosition(this.runs, run);
        return position === null
          ? this.localize.term("searchManager_rebuildsStarting")
          : this.localize.term("searchManager_rebuildsWaiting", position);
      }
      case "Running":
        if (run.documentsWritten == null && !run.zeroDowntime)
          return this.localize.term("searchManager_rebuildsStarting");
        if (run.zeroDowntime) return this.localize.term("searchManager_rebuildsRunningZeroDowntime", percent);
        return percent === null
          ? this.localize.term("searchManager_rebuildsRunningNoEstimate", run.documentsWritten ?? null)
          : this.localize.term(
              "searchManager_rebuildsRunning",
              run.documentsWritten ?? null,
              run.estimatedTotal ?? null,
              percent,
            );
      case "Succeeded":
        // An index with nothing to index (no members, no media) is a normal outcome; say so.
        if (finishedEmpty(run))
          return this.localize.term(
            "searchManager_rebuildsSucceededEmpty",
            formatDuration(run.startedAt ?? run.queuedAt, run.endedAt),
          );
        return this.localize.term(
          "searchManager_rebuildsSucceeded",
          formatDuration(run.startedAt ?? run.queuedAt, run.endedAt),
          run.documentsAfter ?? null,
        );
      case "Failed":
        return this.localize.term(
          "searchManager_rebuildsFailed",
          formatDuration(run.startedAt ?? run.queuedAt, run.endedAt),
          run.error ?? "",
        );
      case "Cancelled":
      default:
        return this.localize.term("searchManager_rebuildsCancelled");
    }
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
      }

      ul {
        list-style: none;
        margin: 0;
        padding: 0;
      }

      li {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
        padding: var(--uui-size-space-3) 0;
        border-top: 1px solid var(--uui-color-divider);
      }

      li:first-child {
        border-top: none;
      }

      .name {
        width: 200px;
        overflow-wrap: anywhere;
      }

      .detail {
        flex: 1 1 220px;
        min-width: 0;
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-1);
      }

      .sub {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
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
    "search-manager-rebuilds-panel": SearchManagerRebuildsPanelElement;
  }
}
