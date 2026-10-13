import { css, customElement, html, nothing, property, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { ExploreService, type DocumentExplanation, type ExploreSchema, type ExploreTarget } from "../api/index.js";
import { describeError } from "../shared/describe-error.js";
import {
  compareExplanations,
  describeContribution,
  flattenContributions,
  formatDifference,
  formatScore,
  type ExplainContext,
  type SentQuery,
} from "./score-explain.js";

/** One side of the comparison, as the results table knows it. */
export interface CompareDocument {
  id: string;
  /** The name, or the id when the document is not on the current page. */
  name: string;
}

/**
 * Compares the scores of two results field by field: one `POST /explore/{target}/explain` with
 * both ids (running the query the last search sent again), then a `uui-table` of what added to
 * each score (A, B and the difference A - B), largest difference first. "Differences only" (a
 * `uui-toggle`) hides the rows both scores share. The header names both documents and their
 * totals. A document that is gone, or no longer matches, says so instead of a column of zeros.
 *
 * Not bound to a manifest; the Explore view renders it in the drawer's place.
 *
 * @element search-manager-score-compare
 * @fires search-manager-compare - `CustomEvent<undefined>` from the close button.
 */
@customElement("search-manager-score-compare")
export class SearchManagerScoreCompareElement extends UmbLitElement {
  /** The target the search ran on. */
  @property({ attribute: false })
  target?: ExploreTarget;

  /** The query the last search sent; the comparison runs it again. */
  @property({ attribute: false })
  query?: SentQuery;

  /** The two documents, A then B. */
  @property({ attribute: false })
  documents?: [CompareDocument, CompareDocument];

  /** The target's schema, for chip labels. */
  @property({ attribute: false })
  schema?: ExploreSchema;

  /** Whether only rows with a difference are listed. */
  @state()
  private _differencesOnly = false;

  @state()
  private _explanations?: [DocumentExplanation, DocumentExplanation];

  @state()
  private _loading = false;

  @state()
  private _error?: string;

  /** Target, ids and query of the comparison shown or loading, so each is asked for once. */
  #loadedKey?: string;
  #request?: AbortController;

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#request?.abort();
    this.#loadedKey = undefined;
  }

  /**
   * Asks for both explanations when the documents, target or query changed.
   *
   * @param changed - The properties that changed.
   */
  override willUpdate(changed: Map<PropertyKey, unknown>): void {
    super.willUpdate(changed);
    const target = this.target;
    const documents = this.documents;
    if (!target || !documents || !this.query) return;
    const ids = documents.map((document) => document.id);
    const key = JSON.stringify([target.name, ids, this.query]);
    if (key === this.#loadedKey) return;
    this.#loadedKey = key;
    void this.#load(target.name, ids, this.query);
  }

  async #load(target: string, ids: Array<string>, query: SentQuery): Promise<void> {
    this.#request?.abort();
    const controller = new AbortController();
    this.#request = controller;
    this._loading = true;
    this._error = undefined;
    this._explanations = undefined;
    const { data, error, response } = await ExploreService.explainExplore({
      path: { target },
      body: { ...query, ids },
      signal: controller.signal,
    }).catch((reason: unknown) => ({ data: undefined, error: reason, response: undefined }));
    if (controller.signal.aborted) return;
    this._loading = false;
    // The server answers in id order; look both up by id all the same.
    const a = data?.documents.find((document) => document.id === ids[0]);
    const b = data?.documents.find((document) => document.id === ids[1]);
    if (a && b) {
      this._explanations = [a, b];
    } else {
      this._error = this.localize.term("searchManager_explainError", describeError(error, response));
      this.#loadedKey = undefined;
    }
  }

  /** "A" or "B". */
  #side(index: number): string {
    return this.localize.term(index === 0 ? "searchManager_compareSideA" : "searchManager_compareSideB");
  }

  #close(): void {
    this.dispatchEvent(new CustomEvent("search-manager-compare", { detail: undefined, bubbles: true, composed: true }));
  }

  /**
   * Renders the header, the toggle and the table.
   *
   * @returns The template.
   */
  override render() {
    const documents = this.documents;
    if (!documents) return nothing;
    return html`
      <uui-box>
        <div slot="headline" class="headline">
          <h4>${this.localize.term("searchManager_compareTitle")}</h4>
          ${documents.map((document, index) => {
            const explanation = this._explanations?.[index];
            const score = explanation?.found && explanation.matched ? formatScore(explanation.score) : "";
            return html`<small
              >${this.localize.term("searchManager_compareDocument", this.#side(index), document.name, score)}</small
            >`;
          })}
        </div>
        <uui-button
          slot="header-actions"
          compact
          look="default"
          label=${this.localize.term("searchManager_compareClose")}
          @click=${this.#close}
        >
          <umb-icon name="icon-wrong"></umb-icon>
        </uui-button>
        ${this.#renderBody(documents)}
      </uui-box>
    `;
  }

  #renderBody(documents: [CompareDocument, CompareDocument]) {
    if (this._loading)
      return html`<uui-loader-bar aria-label=${this.localize.term("searchManager_explainLoading")}></uui-loader-bar>`;
    if (this._error) return html`<p class="error" role="alert">${this._error}</p>`;
    const explanations = this._explanations;
    if (!explanations) return nothing;

    // A side that is gone or no longer matches says why; its column would only be zeros.
    const problems = explanations.flatMap((explanation, index) => {
      const reason = !explanation.found
        ? this.localize.term("searchManager_explainNotFound")
        : !explanation.matched
          ? this.localize.term("searchManager_explainNotMatched")
          : undefined;
      return reason
        ? [this.localize.term("searchManager_compareProblem", this.#side(index), documents[index]!.name, reason)]
        : [];
    });
    if (problems.length > 0) return problems.map((problem) => html`<p class="note">${problem}</p>`);

    const context: ExplainContext = {
      filters: this.query?.filters ?? [],
      schema: this.schema,
      term: (key: string, ...args: Array<string>) => this.localize.term(key, ...args),
    };
    const rows = compareExplanations(
      flattenContributions(explanations[0].root),
      flattenContributions(explanations[1].root),
      this._differencesOnly,
    );
    return html`
      <uui-toggle
        label=${this.localize.term("searchManager_compareDifferencesOnly")}
        .checked=${this._differencesOnly}
        @change=${(event: Event) => (this._differencesOnly = (event.target as HTMLInputElement).checked)}
      ></uui-toggle>
      ${
        rows.length === 0
          ? html`<p class="note">${this.localize.term("searchManager_compareNone")}</p>`
          : html`<uui-table aria-label=${this.localize.term("searchManager_compareTableLabel")}>
              <uui-table-head>
                <uui-table-head-cell>${this.localize.term("searchManager_compareWhat")}</uui-table-head-cell>
                <uui-table-head-cell class="number">${this.#side(0)}</uui-table-head-cell>
                <uui-table-head-cell class="number">${this.#side(1)}</uui-table-head-cell>
                <uui-table-head-cell class="number"
                  >${this.localize.term("searchManager_compareDifference")}</uui-table-head-cell
                >
              </uui-table-head>
              ${rows.map(
                (row) =>
                  html`<uui-table-row>
                    <uui-table-cell>${describeContribution(row, context)}</uui-table-cell>
                    <uui-table-cell class="number">${formatScore(row.a)}</uui-table-cell>
                    <uui-table-cell class="number">${formatScore(row.b)}</uui-table-cell>
                    <uui-table-cell class="number">${formatDifference(row.difference)}</uui-table-cell>
                  </uui-table-row>`,
              )}
            </uui-table>`
      }
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

      .headline small {
        display: block;
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
        overflow-wrap: anywhere;
        font-variant-numeric: tabular-nums;
      }

      uui-toggle {
        display: block;
        margin-bottom: var(--uui-size-space-3);
      }

      uui-table-cell {
        overflow-wrap: anywhere;
      }

      .number {
        font-family: monospace;
        font-variant-numeric: tabular-nums;
        text-align: right;
        white-space: nowrap;
      }

      .note {
        color: var(--uui-color-text-alt);
      }

      .error {
        color: var(--uui-color-danger);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-score-compare": SearchManagerScoreCompareElement;
  }
}
