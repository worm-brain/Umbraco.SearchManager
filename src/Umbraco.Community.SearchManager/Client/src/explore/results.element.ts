import { css, customElement, html, nothing, property, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { ExploreHit, ExploreResult, ExploreSchema } from "../api/index.js";
import { ticksToDate } from "./document-fields.js";
import { editorPath } from "./editor-link.js";
import { PAGE_SIZE } from "./explore-state.js";
import { resolveField } from "./filters.js";

/**
 * The Explore results: a count, a column picker fed from the index's full field list, a
 * `uui-table` (score, id, name, document type, cultures, updated, plus picked fields) and
 * paging. Values from the index are rendered as text.
 *
 * The name opens the document drawer; the icon beside it links to the item's editor. Not bound
 * to a manifest; the Explore view renders it and owns the page, columns and open document.
 *
 * @element search-manager-results
 * @fires search-manager-open-document - `CustomEvent<string | undefined>` with the hit id to show, or none to close.
 * @fires search-manager-page-change - `CustomEvent<number>` with the page to load, from 1.
 * @fires search-manager-columns-change - `CustomEvent<Array<string>>` with the picked extra columns.
 */
@customElement("search-manager-results")
export class SearchManagerResultsElement extends UmbLitElement {
  /** The last search's answer. */
  @property({ attribute: false })
  result?: ExploreResult;

  /** The target's schema, for the column picker and date columns. */
  @property({ attribute: false })
  schema?: ExploreSchema;

  /** Extra columns: field names. */
  @property({ attribute: false })
  columns: Array<string> = [];

  /** The current page, from 1. */
  @property({ type: Number })
  page = 1;

  /** The id of the document open in the drawer. */
  @property({ attribute: false })
  openDocument?: string;

  @state()
  private _columnFilter = "";

  #fire<T>(type: string, detail: T): void {
    this.dispatchEvent(new CustomEvent<T>(type, { detail, bubbles: true, composed: true }));
  }

  #toggleColumn(field: string, on: boolean): void {
    const columns = on ? [...this.columns, field] : this.columns.filter((column) => column !== field);
    this.#fire("search-manager-columns-change", columns);
  }

  /**
   * Renders the header, table and paging.
   *
   * @returns The template.
   */
  override render() {
    const result = this.result;
    if (!result) return nothing;
    const pages = Math.max(1, Math.ceil(result.total / PAGE_SIZE));
    const maxScore = Math.max(0, ...result.hits.map((hit) => hit.score ?? 0));
    // Results sorted newest first (no ranking clause) come back without scores.
    const ranked = result.hits.some((hit) => hit.score != null);

    return html`
      <div class="header">
        <strong role="status">
          ${
            ranked || result.total === 0
              ? this.localize.term("searchManager_resultsCount", result.total)
              : this.localize.term("searchManager_resultsWhole", result.total)
          }
        </strong>
        ${this.#renderColumnPicker()}
      </div>
      ${
        result.hits.length === 0
          ? html`<p class="empty">${this.localize.term("searchManager_resultsNone")}</p>`
          : html`
              <div class="scroll">
                <uui-table aria-label=${this.localize.term("searchManager_resultsLabel")}>
                  <uui-table-head>
                    <uui-table-head-cell>${this.localize.term("searchManager_columnScore")}</uui-table-head-cell>
                    <uui-table-head-cell>${this.localize.term("searchManager_columnId")}</uui-table-head-cell>
                    <uui-table-head-cell>${this.localize.term("searchManager_columnName")}</uui-table-head-cell>
                    <uui-table-head-cell>${this.localize.term("searchManager_columnDocumentType")}</uui-table-head-cell>
                    <uui-table-head-cell>${this.localize.term("searchManager_columnCultures")}</uui-table-head-cell>
                    <uui-table-head-cell>${this.localize.term("searchManager_columnUpdated")}</uui-table-head-cell>
                    ${this.columns.map((column) => html`<uui-table-head-cell class="mono">${column}</uui-table-head-cell>`)}
                  </uui-table-head>
                  ${result.hits.map((hit) => this.#renderRow(hit, maxScore, ranked))}
                </uui-table>
              </div>
            `
      }
      ${
        pages > 1
          ? html`<uui-pagination
              .current=${this.page}
              .total=${pages}
              @change=${(event: Event) =>
                this.#fire("search-manager-page-change", (event.target as HTMLElement & { current: number }).current)}
            ></uui-pagination>`
          : nothing
      }
    `;
  }

  #renderRow(hit: ExploreHit, maxScore: number, ranked: boolean) {
    const name = hit.name ?? hit.id;
    const path = editorPath(hit);
    const open = this.openDocument === hit.id;
    const score = ranked && hit.score != null ? hit.score : undefined;
    return html`
      <uui-table-row class=${open ? "open" : ""}>
        <uui-table-cell class="nowrap">
          ${
            score === undefined
              ? nothing
              : html`<span class="score">
                  <span class="bar" aria-hidden="true"
                    ><span style=${`width: ${maxScore > 0 ? Math.round((score / maxScore) * 100) : 0}%`}></span
                  ></span>
                  ${score.toFixed(3)}
                </span>`
          }
        </uui-table-cell>
        <uui-table-cell class="mono nowrap">${hit.id}</uui-table-cell>
        <uui-table-cell class="name-cell">
          <span class="name">
            <button
              type="button"
              class="open"
              aria-pressed=${open ? "true" : "false"}
              aria-label=${this.localize.term("searchManager_showDocument", name)}
              @click=${() => this.#fire("search-manager-open-document", open ? undefined : hit.id)}
            >
              ${name}
            </button>
            ${
              path
                ? html`<a href=${path} title=${this.localize.term("searchManager_openInEditor", name)}
                    ><umb-icon name="icon-edit"></umb-icon
                    ><span class="sr-only">${this.localize.term("searchManager_openInEditor", name)}</span></a
                  >`
                : nothing
            }
          </span>
        </uui-table-cell>
        <uui-table-cell>${hit.documentType ?? ""}</uui-table-cell>
        <uui-table-cell class="nowrap">
          ${hit.cultures.length > 0 ? hit.cultures.join(", ") : this.localize.term("searchManager_invariant")}
        </uui-table-cell>
        <uui-table-cell class="nowrap">${hit.updated ? hit.updated.slice(0, 16).replace("T", " ") : ""}</uui-table-cell>
        ${this.columns.map((column) => html`<uui-table-cell>${this.#cell(hit, column)}</uui-table-cell>`)}
      </uui-table-row>
    `;
  }

  /** A picked column's text: values joined, dates stored as ticks shown as dates. */
  #cell(hit: ExploreHit, column: string): string {
    const values = hit.fields[column] ?? [];
    const isDate = resolveField(this.schema, column)?.type === "Date";
    return values.map((value) => (isDate ? (ticksToDate(value) ?? value) : value)).join(", ");
  }

  #renderColumnPicker() {
    const filter = this._columnFilter.trim().toLowerCase();
    const fields = (this.schema?.fields ?? []).filter((field) => !filter || field.name.toLowerCase().includes(filter));
    return html`
      <uui-button look="secondary" popovertarget="columns" label=${this.localize.term("searchManager_columns")}>
        <umb-icon name="icon-columns"></umb-icon> ${this.localize.term("searchManager_columns")}
      </uui-button>
      <uui-popover-container id="columns" placement="bottom-end">
        <umb-popover-layout>
          <div class="columns">
            <small>${this.localize.term("searchManager_columnsHint")}</small>
            <uui-input
              label=${this.localize.term("searchManager_columnsFilter")}
              placeholder=${this.localize.term("searchManager_columnsFilter")}
              .value=${this._columnFilter}
              @input=${(event: Event) => (this._columnFilter = String((event.target as HTMLInputElement).value))}
            ></uui-input>
            <div class="column-list">
              ${fields.map(
                (field) =>
                  html`<uui-checkbox
                    label=${field.name}
                    .checked=${this.columns.includes(field.name)}
                    @change=${(event: Event) => this.#toggleColumn(field.name, (event.target as HTMLInputElement).checked)}
                    >${field.name}</uui-checkbox
                  >`,
              )}
            </div>
          </div>
        </umb-popover-layout>
      </uui-popover-container>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
        min-width: 0;
      }

      .header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--uui-size-space-4);
        margin-bottom: var(--uui-size-space-3);
      }

      .scroll {
        overflow-x: auto;
      }

      .empty {
        color: var(--uui-color-text-alt);
      }

      .mono {
        font-family: monospace;
      }

      .nowrap {
        white-space: nowrap;
      }

      uui-table-row.open {
        background: var(--uui-color-surface-emphasis);
      }

      uui-table {
        min-width: 48rem;
      }

      /* Long values (keys, paths) break between words first, then anywhere. */
      uui-table-cell {
        overflow-wrap: break-word;
      }

      .name-cell {
        min-width: 12rem;
      }

      .score {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        font-variant-numeric: tabular-nums;
      }

      .bar {
        display: inline-block;
        width: var(--uui-size-12);
        height: var(--uui-size-1);
        background: var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        overflow: hidden;
      }

      .bar span {
        display: block;
        height: 100%;
        background: var(--uui-color-interactive);
      }

      .name {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      button.open {
        font: inherit;
        font-weight: bold;
        color: var(--uui-color-interactive);
        background: none;
        border: none;
        padding: 0;
        cursor: pointer;
        text-align: left;
        text-decoration: underline;
      }

      button.open:focus-visible,
      .name a:focus-visible {
        outline: 2px solid var(--uui-color-focus);
      }

      .name a {
        color: var(--uui-color-text-alt);
      }

      .sr-only {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip-path: inset(50%);
        white-space: nowrap;
      }

      uui-pagination {
        display: block;
        margin-top: var(--uui-size-space-4);
      }

      .columns {
        display: grid;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-4);
        width: var(--uui-size-100);
      }

      .column-list {
        display: grid;
        gap: var(--uui-size-space-1);
        max-height: 50vh;
        overflow-y: auto;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-results": SearchManagerResultsElement;
  }
}
