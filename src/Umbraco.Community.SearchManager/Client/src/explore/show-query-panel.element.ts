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
  type CompiledQuery,
  type ExploreSchema,
  type ExploreTarget,
  type Filter,
} from "../api/index.js";
import { describeError } from "../shared/describe-error.js";
import { describeFilter } from "./chip-format.js";

/** The `language` of a compiled query that is a C# request rather than query text (ADR 0016). */
const REQUEST_LANGUAGE = "C#";

/** Wait after a change before compiling, so typing a chip value does not send a request per key. */
const COMPILE_DEBOUNCE_MS = 150;

/**
 * The show-query panel: what Explore sends, three ways, each with Copy:
 *
 * - the Lucene query sent to Examine;
 * - the query as Lucene parsed it (`Query.ToString()`), which shows analysis (lower-casing,
 *   numeric ranges) and the default fields bare words search;
 * - C# with Examine: the fluent API when every chip has a fluent form, otherwise `NativeQuery`.
 *
 * For an engine that takes a request object rather than query text (Umbraco Search, whose
 * `language` is `C#`), the panel shows that request once, as C#.
 *
 * Compiled with `POST /explore/{target}/compile` whenever the chips, culture or raw query
 * change. Chips the target cannot run are listed under the query as not sent.
 *
 * @element search-manager-show-query-panel
 */
@customElement("search-manager-show-query-panel")
export class SearchManagerShowQueryPanelElement extends UmbLitElement {
  /** The target. */
  @property({ attribute: false })
  target?: ExploreTarget;

  /** Its schema, for chip labels in the not-sent list. */
  @property({ attribute: false })
  schema?: ExploreSchema;

  /** The chips that run. */
  @property({ attribute: false })
  filters: Array<Filter> = [];

  /** Chips not sent, with the reason, for the list under the query. */
  @property({ attribute: false })
  notSent: Array<{ filter: Filter; reason: string }> = [];

  /** The culture picked. */
  @property({ attribute: false })
  culture?: string;

  /** The raw query in native mode, or undefined. */
  @property({ attribute: false })
  native?: string;

  @state()
  private _compiled?: CompiledQuery;

  @state()
  private _error?: string;

  @state()
  private _copied?: string;

  #timer?: ReturnType<typeof setTimeout>;
  #request?: AbortController;

  /** Compiles again when anything the query depends on changes. */
  protected override updated(changed: PropertyValues<this>): void {
    super.updated(changed);
    if (changed.has("target") || changed.has("filters") || changed.has("culture") || changed.has("native")) {
      clearTimeout(this.#timer);
      this.#timer = setTimeout(() => void this.#compile(), COMPILE_DEBOUNCE_MS);
    }
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    clearTimeout(this.#timer);
    this.#request?.abort();
  }

  async #compile(): Promise<void> {
    if (!this.target) return;
    this.#request?.abort();
    const controller = new AbortController();
    this.#request = controller;
    const { data, error, response } = await ExploreService.compileExplore({
      path: { target: this.target.name },
      body: { filters: this.filters, culture: this.culture ?? null, native: this.native ?? null },
      signal: controller.signal,
    }).catch((reason: unknown) => ({ data: undefined, error: reason, response: undefined }));
    if (controller.signal.aborted) return;
    this._compiled = data;
    this._error = data ? undefined : describeError(error, response);
  }

  async #copy(key: string, text: string): Promise<void> {
    await navigator.clipboard.writeText(text);
    this._copied = key;
    setTimeout(() => (this._copied = this._copied === key ? undefined : this._copied), 2000);
  }

  /**
   * Renders the three blocks and the not-sent list.
   *
   * @returns The template.
   */
  override render() {
    if (this._error)
      return html`<p class="error" role="alert">${this.localize.term("searchManager_queryError", this._error)}</p>`;
    const compiled = this._compiled;
    if (!compiled) return html`<uui-loader-bar></uui-loader-bar>`;

    const term = (key: string, ...args: Array<string>) => this.localize.term(key, ...args);
    const notSent = this.notSent.map(
      ({ filter, reason }) =>
        html`<div class="not-sent">
          ${this.localize.term("searchManager_queryNotSent", describeFilter(filter, this.schema, term).label, reason)}
        </div>`,
    );
    // An engine that takes a request object (Umbraco Search) sends C# itself: one block.
    if (compiled.language === REQUEST_LANGUAGE) {
      return html`
        <div class="blocks" role="region" aria-label=${this.localize.term("searchManager_queryPanel")}>
          ${this.#block(
            "csharp",
            this.localize.term("searchManager_queryRequest", this.target?.name ?? ""),
            compiled.code,
            this.localize.term("searchManager_queryRequestNote"),
          )}
          ${notSent}
        </div>
      `;
    }
    return html`
      <div class="blocks" role="region" aria-label=${this.localize.term("searchManager_queryPanel")}>
        ${this.#block("lucene", this.localize.term("searchManager_queryLucene", compiled.language), compiled.query)}
        ${this.#block(
          "parsed",
          this.localize.term("searchManager_queryParsed"),
          compiled.parsed ?? this.localize.term("searchManager_queryNotParsed"),
          this.culture ? this.localize.term("searchManager_queryCultureNote", this.culture.toLowerCase()) : "",
        )}
        ${this.#block(
          "csharp",
          this.localize.term(
            compiled.fluent || this.native !== undefined
              ? "searchManager_queryCSharp"
              : "searchManager_queryCSharpNative",
          ),
          compiled.code,
        )}
        ${notSent}
      </div>
    `;
  }

  #block(key: string, title: string, text: string, note = "") {
    return html`
      <section class="block">
        <div class="head">
          <strong>${title}</strong>
          <uui-button
            compact
            look="secondary"
            label=${this.localize.term(this._copied === key ? "searchManager_documentCopied" : "searchManager_queryCopy")}
            @click=${() => void this.#copy(key, text)}
          ></uui-button>
        </div>
        <pre>${text}</pre>
        ${note ? html`<small>${note}</small>` : nothing}
      </section>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
      }

      .blocks {
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface-alt);
      }

      .block {
        display: grid;
        gap: var(--uui-size-space-1);
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border-bottom: 1px solid var(--uui-color-divider);
      }

      .head {
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: var(--uui-size-space-3);
        font-size: var(--uui-type-small-size);
      }

      pre {
        margin: 0;
        font-family: monospace;
        white-space: pre-wrap;
        overflow-wrap: anywhere;
      }

      small,
      .not-sent {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      .not-sent {
        padding: var(--uui-size-space-2) var(--uui-size-space-4);
      }

      .error {
        color: var(--uui-color-danger);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-show-query-panel": SearchManagerShowQueryPanelElement;
  }
}
