import { css, customElement, html, nothing, property } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { IndexPresence } from "../../api/index.js";
import { engineLabel, formatCount } from "../../indexes/index-format.js";
import { DATE_TIME } from "../../shared/backoffice-floor.js";
import { exploreUrl } from "../../shared/explore-links.js";
import { culturesText, presenceLook, viewFieldsLink } from "./node-model.js";

/**
 * Which indexes hold one node: a `uui-table` with a row per index, giving the index and its
 * engine, the status as an icon and a word (Present, Missing, Not applicable; never colour
 * alone), the documents and cultures the index holds for the node, why it is missing or not
 * applicable, a "Stale" tag when the index copy is older than the node, and a "View fields" link
 * into Explore for the rows that hold it.
 *
 * Not bound to a manifest; the Find a node tool and the "Show in search indexes" sidebar render
 * it. Each row carries `data-index` with the index name, for tests.
 *
 * @element search-manager-node-presence
 */
@customElement("search-manager-node-presence")
export class SearchManagerNodePresenceElement extends UmbLitElement {
  /** One entry per index, as `GET /diagnose/nodes/{key}` returns them. */
  @property({ attribute: false })
  indexes: Array<IndexPresence> = [];

  /**
   * Renders the table.
   *
   * @returns The template.
   */
  override render() {
    if (this.indexes.length === 0) return html`<p class="sub">${this.localize.term("searchManager_nodeNoIndexes")}</p>`;
    // The table can be wider than the sidebar; it scrolls on its own.
    return html`
      <div class="scroll">
        <uui-table aria-label=${this.localize.term("searchManager_nodePresenceLabel")}>
          <uui-table-head>
            <uui-table-head-cell>${this.localize.term("searchManager_columnIndex")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("searchManager_nodeColumnStatus")}</uui-table-head-cell>
            <uui-table-head-cell class="number"
              >${this.localize.term("searchManager_columnDocuments")}</uui-table-head-cell
            >
            <uui-table-head-cell>${this.localize.term("searchManager_columnCultures")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("searchManager_nodeColumnReason")}</uui-table-head-cell>
            <uui-table-head-cell></uui-table-head-cell>
          </uui-table-head>
          ${this.indexes.map((index) => this.#renderRow(index))}
        </uui-table>
      </div>
    `;
  }

  /**
   * Renders one index's row.
   *
   * @param index - The index's presence.
   * @returns The template.
   */
  #renderRow(index: IndexPresence) {
    const look = presenceLook(index.status);
    const link = viewFieldsLink(index);
    return html`
      <uui-table-row data-index=${index.target}>
        <uui-table-cell>
          <strong>${index.target}</strong>
          <div class="sub">${engineLabel(index.engine)}</div>
        </uui-table-cell>
        <uui-table-cell>
          <div class="status">
            <umb-icon name=${look.icon} style=${`color: ${look.color}`}></umb-icon>
            <span>${this.localize.term(look.label)}</span>
            ${
              index.stale === true
                ? html`<uui-tag look="secondary" color="warning" title=${this.#staleTitle(index)}
                    >${this.localize.term("searchManager_nodeStale")}</uui-tag
                  >`
                : nothing
            }
          </div>
        </uui-table-cell>
        <uui-table-cell class="number"
          >${index.status === "Present" ? formatCount(index.documentCount) : "-"}</uui-table-cell
        >
        <uui-table-cell class="mono">${culturesText(index.cultures) || "-"}</uui-table-cell>
        <uui-table-cell class="sub">${index.reason ?? ""}</uui-table-cell>
        <uui-table-cell>
          ${
            link
              ? html`<uui-button
                  compact
                  look="secondary"
                  href=${exploreUrl(link.target, link)}
                  label=${this.localize.term("searchManager_nodeViewFields", index.target)}
                  >${this.localize.term("searchManager_nodeViewFieldsShort")}</uui-button
                >`
              : nothing
          }
        </uui-table-cell>
      </uui-table-row>
    `;
  }

  /**
   * The tooltip of the "Stale" tag: the index copy's date against the node's.
   *
   * @param index - The index's presence.
   * @returns The text.
   */
  #staleTitle(index: IndexPresence): string {
    const date = (value: string | null | undefined) => (value ? this.localize.date(value, DATE_TIME) : "?");
    return this.localize.term(
      "searchManager_nodeStaleTitle",
      date(index.indexUpdateDate),
      date(index.expectedUpdateDate),
    );
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
      }

      .scroll {
        overflow-x: auto;
      }

      uui-table-cell {
        vertical-align: top;
      }

      .sub {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      .mono {
        font-family: monospace;
      }

      .number {
        text-align: right;
        font-variant-numeric: tabular-nums;
      }

      .status {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-2);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-node-presence": SearchManagerNodePresenceElement;
  }
}
