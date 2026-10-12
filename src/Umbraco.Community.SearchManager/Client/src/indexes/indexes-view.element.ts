import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { IndexesService } from "../api/sdk.gen.js";
import type { IndexSummary } from "../api/types.gen.js";
import { describeError } from "../shared/describe-error.js";
import { engineLabel, formatBytes, formatCount, healthLook } from "./index-format.js";

/**
 * The Indexes view: every index the user may see, from every source, with its health and the
 * counts the core Examine dashboard only shows one index at a time.
 *
 * Loads `GET /indexes` when connected and on "Reload". Rows are a `uui-table`; health is an icon
 * plus a text label (and the source's message for an unhealthy index), so colour is never the
 * only signal. Counts a source cannot give show as a dash.
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

  /** Loads the indexes as soon as the view is in the page. */
  override connectedCallback(): void {
    super.connectedCallback();
    void this.#load();
  }

  /** Fetches the index list, keeping the previous rows while it loads. */
  async #load(): Promise<void> {
    this._loading = true;
    const { data, error, response } = await IndexesService.getIndexes();
    this._loading = false;
    if (data) {
      this._indexes = data;
      this._error = undefined;
    } else {
      this._error = describeError(error, response);
    }
  }

  /**
   * Renders the toolbar and the table.
   *
   * @returns The template.
   */
  override render() {
    return html`
      <uui-box>
        <div slot="header" class="toolbar">
          <span>${this.localize.term("searchManager_indexesCount", this._indexes.length)}</span>
          <uui-button
            look="secondary"
            label=${this.localize.term("searchManager_reload")}
            .state=${this._loading ? "waiting" : undefined}
            @click=${() => void this.#load()}
          ></uui-button>
        </div>
        ${
          this._error
            ? html`<p class="error" role="alert">${this.localize.term("searchManager_indexesError", this._error)}</p>`
            : nothing
        }
        ${this.#renderTable()}
      </uui-box>
    `;
  }

  /**
   * Renders the index table, or a loader before the first answer arrives.
   *
   * @returns The template.
   */
  #renderTable() {
    if (this._loading && this._indexes.length === 0) return html`<uui-loader-bar></uui-loader-bar>`;
    if (this._indexes.length === 0) return nothing;

    return html`
      <uui-table aria-label=${this.localize.term("searchManager_indexesTableLabel")}>
        <uui-table-head>
          <uui-table-head-cell>${this.localize.term("searchManager_columnIndex")}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term("searchManager_columnHealth")}</uui-table-head-cell>
          <uui-table-head-cell class="number"
            >${this.localize.term("searchManager_columnDocuments")}</uui-table-head-cell
          >
          <uui-table-head-cell class="number">${this.localize.term("searchManager_columnDeleted")}</uui-table-head-cell>
          <uui-table-head-cell class="number">${this.localize.term("searchManager_columnFields")}</uui-table-head-cell>
          <uui-table-head-cell class="number">${this.localize.term("searchManager_columnSize")}</uui-table-head-cell>
        </uui-table-head>
        ${this._indexes.map((index) => this.#renderRow(index))}
      </uui-table>
    `;
  }

  /**
   * Renders one index row.
   *
   * @param index - The index.
   * @returns The row template.
   */
  #renderRow(index: IndexSummary) {
    const look = healthLook(index.health);
    return html`
      <uui-table-row>
        <uui-table-cell>
          <div class="name">
            <strong>${index.name}</strong>
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
              <div>${this.localize.term(`searchManager_health${index.health}`)}</div>
              ${index.healthMessage ? html`<div class="sub">${index.healthMessage}</div>` : nothing}
            </div>
          </div>
        </uui-table-cell>
        <uui-table-cell class="number">${formatCount(index.documentCount)}</uui-table-cell>
        <uui-table-cell class="number">${formatCount(index.deletedDocumentCount)}</uui-table-cell>
        <uui-table-cell class="number">${formatCount(index.fieldCount)}</uui-table-cell>
        <uui-table-cell class="number">${formatBytes(index.sizeInBytes)}</uui-table-cell>
      </uui-table-row>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
        padding: var(--uui-size-layout-1);
      }

      .toolbar {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--uui-size-space-4);
        width: 100%;
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

      .number {
        text-align: right;
        font-variant-numeric: tabular-nums;
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
