import { css, customElement, html, nothing, property, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import {
  ExploreService,
  type DocumentExplanation,
  type ExploreHit,
  type ExploreSchema,
  type ExploreTarget,
} from "../api/index.js";
import { describeError } from "../shared/describe-error.js";
import { documentJson, filterForValue, groupFields, ticksToDate, type FieldGroupKey } from "./document-fields.js";
import { editorPath } from "./editor-link.js";
import { SearchManagerAddFilterEvent } from "./explore-events.js";
import { resolveField } from "./filters.js";
import { explainUnavailableReason, type SentQuery } from "./score-explain.js";
import "./score-explanation.element.js";

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
 * "Explain score" (a toggle; `aria-pressed` follows {@link explain}) asks
 * `POST /explore/{target}/explain` why the document scored what it did, running the query the
 * last search sent again ({@link query}), and shows the answer as a
 * `search-manager-score-explanation` tree. It is disabled, with the reason as visible text, on a
 * target without `explainScore` and for results that are not ranked (filters only, newest first).
 *
 * Not bound to a manifest; the Explore view renders it beside the results.
 *
 * @element search-manager-document-drawer
 * @fires search-manager-add-filter - {@link SearchManagerAddFilterEvent} from the filter buttons.
 * @fires search-manager-open-document - `CustomEvent<undefined>` from the close button.
 * @fires search-manager-explain-toggle - `CustomEvent<boolean>` from "Explain score": whether to show the explanation.
 */
@customElement("search-manager-document-drawer")
export class SearchManagerDocumentDrawerElement extends UmbLitElement {
  /** The document. */
  @property({ attribute: false })
  hit?: ExploreHit;

  /** The target's schema, for field types and cultures. */
  @property({ attribute: false })
  schema?: ExploreSchema;

  /** The target: its name is shown under the title, its capabilities decide "Explain score". */
  @property({ attribute: false })
  target?: ExploreTarget;

  /** The query the last search sent, which the explanation runs again. */
  @property({ attribute: false })
  query?: SentQuery;

  /** Whether the score explanation is open; the Explore view owns it (`ex=1` in the URL). */
  @property({ type: Boolean })
  explain = false;

  @state()
  private _copied = false;

  /** The explanation of the open document, for {@link #explainedKey}. */
  @state()
  private _explanation?: DocumentExplanation;

  @state()
  private _explainLoading = false;

  @state()
  private _explainError?: string;

  /** Target, document and query of the explanation shown or loading, so each is asked for once. */
  #explainedKey?: string;
  #explainRequest?: AbortController;

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#explainRequest?.abort();
    // Reconnected, the explanation is asked for again.
    this.#explainedKey = undefined;
  }

  /**
   * Asks for the explanation once it is open and the document, target or query changed.
   *
   * @param changed - The properties that changed.
   */
  override willUpdate(changed: Map<PropertyKey, unknown>): void {
    super.willUpdate(changed);
    const hit = this.hit;
    const target = this.target;
    if (!this.explain || !hit || !target || !this.query || this.#explainBlocked()) return;
    const key = JSON.stringify([target.name, hit.id, this.query]);
    if (key === this.#explainedKey) return;
    this.#explainedKey = key;
    void this.#loadExplanation(target.name, hit.id, this.query);
  }

  async #loadExplanation(target: string, id: string, query: SentQuery): Promise<void> {
    this.#explainRequest?.abort();
    const controller = new AbortController();
    this.#explainRequest = controller;
    this._explainLoading = true;
    this._explainError = undefined;
    this._explanation = undefined;
    const { data, error, response } = await ExploreService.explainExplore({
      path: { target },
      body: { ...query, ids: [id] },
      signal: controller.signal,
    }).catch((reason: unknown) => ({ data: undefined, error: reason, response: undefined }));
    // A newer document or query replaced this request while it ran.
    if (controller.signal.aborted) return;
    this._explainLoading = false;
    const explanation = data?.documents.find((document) => document.id === id);
    if (explanation) {
      this._explanation = explanation;
    } else {
      this._explainError = this.localize.term("searchManager_explainError", describeError(error, response));
      // Closing and reopening the explanation asks again.
      this.#explainedKey = undefined;
    }
  }

  /** Why "Explain score" is disabled: the target cannot explain, or the results are not ranked. */
  #explainBlocked(): string | undefined {
    const term = (key: string, ...args: Array<string>) => this.localize.term(key, ...args);
    return (
      explainUnavailableReason(this.target, term) ??
      (this.hit?.score == null ? this.localize.term("searchManager_explainNotRanked") : undefined)
    );
  }

  #toggleExplain(): void {
    this.dispatchEvent(
      new CustomEvent<boolean>("search-manager-explain-toggle", {
        detail: !this.explain,
        bubbles: true,
        composed: true,
      }),
    );
  }

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
    const blocked = this.#explainBlocked();
    return html`
      <uui-box>
        <div slot="headline" class="headline">
          <h4>${name}</h4>
          <small>${[this.target?.name, `id ${hit.id}`, hit.documentType].filter(Boolean).join(" · ")}</small>
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
          ${this.#renderExplainButton(blocked)}
        </div>
        ${blocked ? html`<p class="note">${blocked}</p>` : this.#renderExplanation(name)}
        ${groupFields(hit.fields, this.schema?.cultures ?? []).map(
          (group) => html`
            <h5>${this.localize.term(GROUP_TITLES[group.key])}</h5>
            <dl>${group.fields.map((field) => this.#renderField(field.name, field.values))}</dl>
          `,
        )}
      </uui-box>
    `;
  }

  /** "Explain score": a toggle, disabled with the reason as its title (and as text below) when blocked. */
  #renderExplainButton(blocked: string | undefined) {
    const open = this.explain && !blocked;
    return html`<uui-button
      look=${open ? "primary" : "secondary"}
      label=${this.localize.term("searchManager_explainButton")}
      title=${blocked ?? ""}
      aria-pressed=${open ? "true" : "false"}
      ?disabled=${!!blocked}
      @click=${this.#toggleExplain}
    ></uui-button>`;
  }

  /** The explanation while it is open: loading, the error, or the tree. */
  #renderExplanation(name: string) {
    if (!this.explain) return nothing;
    if (this._explainLoading)
      return html`<uui-loader-bar aria-label=${this.localize.term("searchManager_explainLoading")}></uui-loader-bar>`;
    if (this._explainError) return html`<p class="error" role="alert">${this._explainError}</p>`;
    return html`<search-manager-score-explanation
      .explanation=${this._explanation}
      .filters=${this.query?.filters ?? []}
      .schema=${this.schema}
      name=${name}
    ></search-manager-score-explanation>`;
  }

  #renderField(name: string, values: Array<string>) {
    const type = resolveField(this.schema, name)?.type;
    const value = values.join(", ");
    const include = type && values.length === 1 ? filterForValue(name, type, values[0]!, false) : undefined;
    const date = type === "Date" && values.length === 1 ? ticksToDate(values[0]!) : undefined;
    return html`
      <div class="row">
        <dt>${name}</dt>
        <!-- The filter buttons sit inside the dd: a dl's row div may hold only dt and dd. -->
        <dd>
          <span class="value">${date ? html`${date}<small class="sub">${value}</small>` : value}</span>
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
        </dd>
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

      .note {
        color: var(--uui-color-text-alt);
      }

      .error {
        color: var(--uui-color-danger);
      }

      search-manager-score-explanation {
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
        grid-template-columns: minmax(8rem, 0.9fr) minmax(0, 1.6fr);
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
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        gap: var(--uui-size-space-2);
        align-items: start;
        margin: 0;
      }

      .value {
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
