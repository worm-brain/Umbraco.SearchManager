import { css, customElement, html, nothing, property } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { DocumentExplanation, ExplainNode, ExploreSchema, Filter } from "../api/index.js";
import { describeMustNotMatch, describeNode, formatScore, type ExplainContext } from "./score-explain.js";

/** Nodes at this depth or deeper start collapsed in a `<details>`, so a long query stays readable. */
const COLLAPSE_DEPTH = 2;

/**
 * One document's score, explained: a nested list from the total down to each word, phrase,
 * prefix and filter, each line a sentence (`nodeName contains "home" 2 times`) with its figures
 * under it and its share of the score in a monospace column. Chips that only remove documents
 * follow the tree, each "adds no score". Deep subtrees and a filter's matching detail are
 * collapsed in native `<details>` elements (UUI has no disclosure element that renders a nested
 * list; `uui-box` and `umb-*` collapsibles are panels, not list items). Everything is text.
 *
 * Not bound to a manifest; the document drawer renders it.
 *
 * @element search-manager-score-explanation
 */
@customElement("search-manager-score-explanation")
export class SearchManagerScoreExplanationElement extends UmbLitElement {
  /** The server's explanation of one document. */
  @property({ attribute: false })
  explanation?: DocumentExplanation;

  /** The filters the search sent; `filterIndex` and `mustNotMatch` index into them. */
  @property({ attribute: false })
  filters: ReadonlyArray<Filter> = [];

  /** The target's schema, for chip labels. */
  @property({ attribute: false })
  schema?: ExploreSchema;

  /** The document's name, for the tree's accessible name. */
  @property()
  name = "";

  get #context(): ExplainContext {
    return {
      filters: this.filters,
      schema: this.schema,
      term: (key: string, ...args: Array<string>) => this.localize.term(key, ...args),
    };
  }

  /**
   * Renders the tree, or why there is none.
   *
   * @returns The template.
   */
  override render() {
    const explanation = this.explanation;
    if (!explanation) return nothing;
    if (!explanation.found) return html`<p class="note">${this.localize.term("searchManager_explainNotFound")}</p>`;
    if (!explanation.matched || !explanation.root)
      return html`<p class="note">${this.localize.term("searchManager_explainNotMatched")}</p>`;

    const context = this.#context;
    const excluded = explanation.mustNotMatch
      .map((index) => describeMustNotMatch(index, context))
      .filter((text): text is string => text !== undefined);
    return html`
      <p class="total">
        ${this.localize.term("searchManager_explainTotal", formatScore(explanation.score ?? explanation.root.value))}
      </p>
      <ul class="tree" aria-label=${this.localize.term("searchManager_explainTreeLabel", this.name)}>
        ${this.#renderNode(explanation.root, 0, context)}
      </ul>
      ${
        excluded.length > 0
          ? html`<ul class="excluded" aria-label=${this.localize.term("searchManager_explainExcludedLabel")}>
              ${excluded.map(
                (text) => html`<li class="line"><span class="score">0</span><span class="text">${text}</span></li>`,
              )}
            </ul>`
          : nothing
      }
    `;
  }

  /** One node and its subtree. */
  #renderNode(node: ExplainNode, depth: number, context: ExplainContext): unknown {
    const sentence = describeNode(node, context);
    const line = html`<div class="line">
      <span class="score">${formatScore(node.value)}</span>
      <span class="text"
        >${sentence.text}${sentence.detail ? html`<small class="detail">${sentence.detail}</small>` : nothing}</span
      >
    </div>`;

    if (node.children.length === 0) return html`<li>${line}</li>`;
    const children = html`<ul>
      ${node.children.map((child) => this.#renderNode(child, depth + 1, context))}
    </ul>`;

    // A filter's child is the clause as Lucene ran it: detail, not part of the score's sum.
    if (node.kind === "Filter") {
      return html`<li>
        ${line}
        <details>
          <summary>${this.localize.term("searchManager_explainFilterDetail")}</summary>
          ${children}
        </details>
      </li>`;
    }
    if (depth + 1 >= COLLAPSE_DEPTH) {
      return html`<li>
        ${line}
        <details>
          <summary>${this.localize.term("searchManager_explainMore", node.children.length)}</summary>
          ${children}
        </details>
      </li>`;
    }
    return html`<li>${line}${children}</li>`;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
      }

      .note {
        color: var(--uui-color-text-alt);
      }

      .total {
        margin: 0 0 var(--uui-size-space-2);
        font-weight: bold;
        font-variant-numeric: tabular-nums;
      }

      ul {
        list-style: none;
        margin: 0;
        padding: 0;
      }

      /* Nested levels indent, with a rule showing which node they belong to. */
      ul ul {
        margin-left: var(--uui-size-space-3);
        padding-left: var(--uui-size-space-3);
        border-left: 1px solid var(--uui-color-divider);
      }

      .line {
        display: grid;
        grid-template-columns: var(--uui-size-20) minmax(0, 1fr);
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-1) 0;
      }

      .score {
        font-family: monospace;
        font-variant-numeric: tabular-nums;
        text-align: right;
      }

      .text {
        overflow-wrap: anywhere;
      }

      .detail {
        display: block;
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
        font-variant-numeric: tabular-nums;
      }

      details > summary {
        cursor: pointer;
        color: var(--uui-color-interactive);
        font-size: var(--uui-type-small-size);
        /* Line up with the sentence column, past the score column. */
        margin-left: calc(var(--uui-size-20) + var(--uui-size-space-3));
      }

      details > summary:focus-visible {
        outline: 2px solid var(--uui-color-focus);
      }

      .excluded {
        margin-top: var(--uui-size-space-3);
        padding-top: var(--uui-size-space-2);
        border-top: 1px solid var(--uui-color-divider);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-score-explanation": SearchManagerScoreExplanationElement;
  }
}
