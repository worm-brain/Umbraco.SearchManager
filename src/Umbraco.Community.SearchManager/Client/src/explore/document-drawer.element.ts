import { css, customElement, html, nothing, property, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { ExploreHit, ExploreSchema } from "../api/index.js";
import { documentJson, filterForValue, groupFields, ticksToDate, type FieldGroupKey } from "./document-fields.js";
import { editorPath } from "./editor-link.js";
import { SearchManagerAddFilterEvent } from "./explore-events.js";
import { resolveField } from "./filters.js";

const GROUP_TITLES: Readonly<Record<FieldGroupKey, string>> = {
  system: "searchManager_groupSystem",
  properties: "searchManager_groupProperties",
  cultures: "searchManager_groupCultures",
  raw: "searchManager_groupRaw",
};

/**
 * The document drawer: every stored field of one result, grouped (system, properties, culture
 * variants, raw copies), each with "filter for" and "filter out" buttons when the field can be
 * filtered on, plus "Open in editor" and "Copy as JSON". Values are text, never markup; dates
 * stored as ticks show the date beside the ticks.
 *
 * Not bound to a manifest; the Explore view renders it beside the results.
 *
 * @element search-manager-document-drawer
 * @fires search-manager-add-filter - {@link SearchManagerAddFilterEvent} from the filter buttons.
 * @fires search-manager-open-document - `CustomEvent<undefined>` from the close button.
 */
@customElement("search-manager-document-drawer")
export class SearchManagerDocumentDrawerElement extends UmbLitElement {
  /** The document. */
  @property({ attribute: false })
  hit?: ExploreHit;

  /** The target's schema, for field types and cultures. */
  @property({ attribute: false })
  schema?: ExploreSchema;

  /** The target's name, shown under the title. */
  @property()
  targetName = "";

  @state()
  private _copied = false;

  async #copy(): Promise<void> {
    if (!this.hit) return;
    await navigator.clipboard.writeText(documentJson(this.hit.id, this.hit.fields));
    this._copied = true;
    setTimeout(() => (this._copied = false), 2000);
  }

  #close(): void {
    this.dispatchEvent(
      new CustomEvent("search-manager-open-document", { detail: undefined, bubbles: true, composed: true }),
    );
  }

  /**
   * Renders the drawer.
   *
   * @returns The template.
   */
  override render() {
    const hit = this.hit;
    if (!hit) return nothing;
    const name = hit.name ?? hit.id;
    const path = editorPath(hit);
    return html`
      <uui-box>
        <div slot="headline" class="headline">
          <h4>${name}</h4>
          <small>${[this.targetName, `id ${hit.id}`, hit.documentType].filter(Boolean).join(" · ")}</small>
        </div>
        <uui-button
          slot="header-actions"
          compact
          look="default"
          label=${this.localize.term("searchManager_documentClose")}
          @click=${this.#close}
        >
          <umb-icon name="icon-wrong"></umb-icon>
        </uui-button>
        <div class="actions">
          ${
            path
              ? html`<uui-button
                  look="secondary"
                  href=${path}
                  label=${this.localize.term("searchManager_documentOpen")}
                ></uui-button>`
              : nothing
          }
          <uui-button
            look="secondary"
            label=${this.localize.term(this._copied ? "searchManager_documentCopied" : "searchManager_documentCopy")}
            @click=${() => void this.#copy()}
          ></uui-button>
        </div>
        ${groupFields(hit.fields, this.schema?.cultures ?? []).map(
          (group) => html`
            <h5>${this.localize.term(GROUP_TITLES[group.key])}</h5>
            <dl>${group.fields.map((field) => this.#renderField(field.name, field.values))}</dl>
          `,
        )}
      </uui-box>
    `;
  }

  #renderField(name: string, values: Array<string>) {
    const type = resolveField(this.schema, name)?.type;
    const value = values.join(", ");
    const include = type && values.length === 1 ? filterForValue(name, type, values[0]!, false) : undefined;
    const date = type === "Date" && values.length === 1 ? ticksToDate(values[0]!) : undefined;
    return html`
      <div class="row">
        <dt>${name}</dt>
        <dd>${date ? html`${date}<small class="sub">${value}</small>` : value}</dd>
        <div class="buttons">
          ${
            include
              ? html`
                  <uui-button
                    compact
                    look="default"
                    label=${this.localize.term("searchManager_documentFilterFor", name, value)}
                    @click=${() => this.dispatchEvent(new SearchManagerAddFilterEvent(include))}
                    ><umb-icon name="icon-zoom-in"></umb-icon
                  ></uui-button>
                  <uui-button
                    compact
                    look="default"
                    color="danger"
                    label=${this.localize.term("searchManager_documentFilterOut", name, value)}
                    @click=${() => this.dispatchEvent(new SearchManagerAddFilterEvent({ ...include, exclude: true }))}
                    ><umb-icon name="icon-zoom-out"></umb-icon
                  ></uui-button>
                `
              : nothing
          }
        </div>
      </div>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
      }

      .headline h4 {
        margin: 0;
        overflow-wrap: anywhere;
      }

      .headline small,
      .sub {
        display: block;
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      .actions {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-2);
        margin-bottom: var(--uui-size-space-4);
      }

      h5 {
        margin: var(--uui-size-space-5) 0 var(--uui-size-space-2);
        text-transform: uppercase;
        font-size: var(--uui-type-small-size);
        color: var(--uui-color-text-alt);
      }

      dl {
        margin: 0;
      }

      .row {
        display: grid;
        grid-template-columns: minmax(8rem, 0.9fr) minmax(0, 1.6fr) auto;
        gap: var(--uui-size-space-2);
        align-items: start;
        padding: var(--uui-size-space-1) 0;
        border-top: 1px solid var(--uui-color-divider);
      }

      dt {
        font-family: monospace;
        font-size: var(--uui-type-small-size);
        overflow-wrap: anywhere;
      }

      dd {
        margin: 0;
        overflow-wrap: anywhere;
        white-space: pre-wrap;
      }

      .buttons {
        display: flex;
        gap: var(--uui-size-space-1);
        --uui-button-height: var(--uui-size-6);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-document-drawer": SearchManagerDocumentDrawerElement;
  }
}
