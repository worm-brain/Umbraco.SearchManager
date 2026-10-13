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
  ExploreService,
  type ExploreSchema,
  type ExploreTarget,
  type FacetsResult,
  type FieldFacet,
  type Filter,
  type IndexField,
} from "../api/index.js";
import { describeError } from "../shared/describe-error.js";
import { newFieldDraft } from "./chip-edit.js";
import { SearchManagerAddFilterEvent, type SearchManagerChipChangeEvent } from "./explore-events.js";
import { fieldLabel, resolveField, sameFilter } from "./filters.js";
import { coverage, valueLabel, valueOperator } from "./suggestions.js";
import "./chip-editor.element.js";

/** The aliases whose fields are pinned at the top of the panel, in order. */
export const PINNED_ALIASES = ["type", "kind", "published", "updated"] as const;

/**
 * The fields panel beside the results:
 *
 * - **Pinned fields** (document type, index type, published, updated, when the target has them)
 *   with their top values from `POST /explore/{target}/facets`: over the whole index when no
 *   chip runs, over the results otherwise, and labelled with which. Dates count relative
 *   ranges. Clicking a value toggles an include chip; its minus button adds an exclude chip.
 * - **All fields:** every field with its type and coverage, filterable; clicking one opens the
 *   chip editor for a new chip on it.
 *
 * Not bound to a manifest; the Explore view renders it and applies its events.
 *
 * @element search-manager-fields-panel
 * @fires search-manager-add-filter - {@link SearchManagerAddFilterEvent} for each picked value or new chip.
 */
@customElement("search-manager-fields-panel")
export class SearchManagerFieldsPanelElement extends UmbLitElement {
  /** The target. */
  @property({ attribute: false })
  target?: ExploreTarget;

  /** Its schema. */
  @property({ attribute: false })
  schema?: ExploreSchema;

  /** The chips that run (disabled ones already left out). */
  @property({ attribute: false })
  filters: Array<Filter> = [];

  /** Every chip, to show which values are already filtered on. */
  @property({ attribute: false })
  allFilters: Array<Filter> = [];

  /** The culture picked. */
  @property({ attribute: false })
  culture?: string;

  @state()
  private _facets?: FacetsResult;

  @state()
  private _error?: string;

  @state()
  private _fieldFilter = "";

  /** The field whose new-chip editor is open. */
  @state()
  private _adding?: IndexField;

  #request?: AbortController;

  /** Counts again when the target, its schema, the running chips or the culture change. */
  protected override updated(changed: PropertyValues<this>): void {
    super.updated(changed);
    if (changed.has("schema") || changed.has("filters") || changed.has("culture")) void this.#load();
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#request?.abort();
  }

  /** The pinned fields the target has. */
  get #pinned(): Array<IndexField> {
    return PINNED_ALIASES.map((alias) => resolveField(this.schema, alias)).filter(
      (field): field is IndexField => field !== undefined,
    );
  }

  async #load(): Promise<void> {
    this.#request?.abort();
    const target = this.target;
    const fields = this.#pinned.map((field) => field.name);
    if (!target || !this.schema || !target.capabilities.termStatistics || fields.length === 0) {
      this._facets = undefined;
      return;
    }

    const controller = new AbortController();
    this.#request = controller;
    const { data, error, response } = await ExploreService.getExploreFacets({
      path: { target: target.name },
      body: { filters: this.filters, culture: this.culture ?? null, fields, take: 6 },
      signal: controller.signal,
    }).catch((reason: unknown) => ({ data: undefined, error: reason, response: undefined }));
    if (controller.signal.aborted) return;
    this._facets = data;
    this._error = data ? undefined : this.localize.term("searchManager_fieldsError", describeError(error, response));
  }

  #valueFilter(facet: FieldFacet, value: string, exclude: boolean): Filter {
    return { kind: "Field", field: facet.field, op: valueOperator(facet.type, value), value, exclude };
  }

  /**
   * Renders the pinned values and the field list.
   *
   * @returns The template.
   */
  override render() {
    if (!this.schema) return nothing;
    return html`
      <uui-box>
        <div slot="headline" class="headline">
          <h4>${this.localize.term("searchManager_fieldsPanel")}</h4>
          ${
            this._facets
              ? html`<small
                  >${
                    this._facets.wholeIndex
                      ? this.localize.term("searchManager_fieldsWholeIndex", this._facets.total)
                      : this.localize.term("searchManager_fieldsResults", this._facets.total)
                  }</small
                >`
              : nothing
          }
        </div>
        ${this._error ? html`<p class="error" role="alert">${this._error}</p>` : nothing}
        ${this._facets?.fields.map((facet) => this.#renderFacet(facet))} ${this.#renderAllFields()}
      </uui-box>
    `;
  }

  #renderFacet(facet: FieldFacet) {
    const label = fieldLabel(this.schema, facet.field);
    const max = Math.max(1, ...facet.values.map((value) => value.count));
    const term = (key: string, ...args: Array<string>) => this.localize.term(key, ...args);
    return html`
      <section class="facet" aria-label=${label}>
        <div class="facet-head">
          <strong>${label}</strong>
          <span class="mono">${facet.field}</span>
        </div>
        ${facet.values.length === 0 ? html`<small class="muted">${this.localize.term("searchManager_fieldsNoValues")}</small>` : nothing}
        ${facet.values.map((value) => {
          // Shown as written; the filter still carries the indexed value.
          const shown = valueLabel(facet.type, value, term);
          const include = this.#valueFilter(facet, value.value, false);
          const on = this.allFilters.some((filter) => sameFilter(filter, include));
          return html`
            <div class="value">
              <button
                type="button"
                class=${on ? "on" : ""}
                aria-pressed=${on ? "true" : "false"}
                aria-label=${this.localize.term("searchManager_fieldsInclude", label, shown)}
                @click=${() => this.dispatchEvent(new SearchManagerAddFilterEvent(include, true))}
              >
                <span class="bar" style=${`width: ${Math.round((value.count / max) * 100)}%`}></span>
                <span class="text">${shown}</span>
                <span class="count">${value.count.toLocaleString("en-GB")}</span>
              </button>
              <uui-button
                compact
                look="outline"
                color="danger"
                label=${this.localize.term("searchManager_fieldsExclude", label, shown)}
                @click=${() =>
                  this.dispatchEvent(new SearchManagerAddFilterEvent(this.#valueFilter(facet, value.value, true)))}
                ><umb-icon name="icon-zoom-out"></umb-icon
              ></uui-button>
            </div>
          `;
        })}
        ${facet.sampled ? html`<small class="muted">${this.localize.term("searchManager_fieldsSampled")}</small>` : nothing}
      </section>
    `;
  }

  #renderAllFields() {
    const schema = this.schema!;
    const query = this._fieldFilter.trim().toLowerCase();
    const fields = schema.fields.filter(
      (field) =>
        !query ||
        field.name.toLowerCase().includes(query) ||
        fieldLabel(schema, field.name).toLowerCase().includes(query),
    );
    return html`
      <section class="all" aria-label=${this.localize.term("searchManager_fieldsAll", String(schema.fields.length))}>
        <strong>${this.localize.term("searchManager_fieldsAll", schema.fields.length.toLocaleString("en-GB"))}</strong>
        <uui-input
          label=${this.localize.term("searchManager_fieldsFilter")}
          placeholder=${this.localize.term("searchManager_fieldsFilter")}
          .value=${this._fieldFilter}
          @input=${(event: Event) => (this._fieldFilter = String((event.target as HTMLInputElement).value))}
        ></uui-input>
        ${
          this._adding
            ? html`<div class="editor">
                <search-manager-chip-editor
                  is-new
                  .schema=${schema}
                  .filter=${newFieldDraft(this._adding.name, this._adding.type, schema)}
                  @search-manager-chip-change=${(event: SearchManagerChipChangeEvent) => {
                    event.stopPropagation();
                    this.dispatchEvent(new SearchManagerAddFilterEvent(event.filter));
                    this._adding = undefined;
                  }}
                  @search-manager-chip-edit-cancel=${() => (this._adding = undefined)}
                ></search-manager-chip-editor>
              </div>`
            : nothing
        }
        <ul class="field-list">
          ${fields.map((field) => {
            const share = coverage(field, schema.documentCount);
            return html`<li>
              <button
                type="button"
                aria-label=${this.localize.term("searchManager_fieldsAdd", field.name)}
                @click=${async () => {
                  this._adding = field;
                  await this.updateComplete;
                  await this.shadowRoot?.querySelector("search-manager-chip-editor")?.focusFirstField();
                }}
              >
                <span class="mono name">${field.name}</span>
                <uui-tag look="secondary">${field.type}</uui-tag>
                <span class="count" title=${share ? this.localize.term("searchManager_fieldsCoverage", share) : ""}
                  >${share}</span
                >
              </button>
            </li>`;
          })}
        </ul>
      </section>
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
      }

      .headline small,
      .muted,
      .mono {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      .mono {
        font-family: monospace;
      }

      .facet {
        display: grid;
        gap: var(--uui-size-space-1);
        margin-bottom: var(--uui-size-space-5);
      }

      .facet-head {
        display: flex;
        justify-content: space-between;
        gap: var(--uui-size-space-2);
        font-size: var(--uui-type-small-size);
      }

      .value {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-1);
        --uui-button-height: var(--uui-size-7);
      }

      .value button {
        position: relative;
        flex: 1;
        min-width: 0;
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-1) var(--uui-size-space-2);
        border: none;
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        text-align: left;
        cursor: pointer;
      }

      .value button.on {
        outline: 1px solid var(--uui-color-selected);
        font-weight: bold;
      }

      .value button:focus-visible,
      .field-list button:focus-visible {
        outline: 2px solid var(--uui-color-focus);
      }

      .bar {
        position: absolute;
        inset: 0 auto 0 0;
        background: var(--uui-color-surface-emphasis);
        border-radius: var(--uui-border-radius);
      }

      .text,
      .count {
        position: relative;
      }

      .text {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }

      .count {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
        font-variant-numeric: tabular-nums;
      }

      .all {
        display: grid;
        gap: var(--uui-size-space-2);
        border-top: 1px solid var(--uui-color-divider);
        padding-top: var(--uui-size-space-4);
      }

      .editor {
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
      }

      .field-list {
        list-style: none;
        margin: 0;
        padding: 0;
        max-height: 40vh;
        overflow-y: auto;
      }

      .field-list button {
        width: 100%;
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto 3rem;
        gap: var(--uui-size-space-2);
        align-items: center;
        padding: var(--uui-size-space-1);
        border: none;
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        text-align: left;
        cursor: pointer;
      }

      .field-list button:hover {
        background: var(--uui-color-surface-emphasis);
      }

      .field-list .name {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        color: var(--uui-color-text);
      }

      .field-list .count {
        text-align: right;
      }

      .error {
        color: var(--uui-color-danger);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-fields-panel": SearchManagerFieldsPanelElement;
  }
}
