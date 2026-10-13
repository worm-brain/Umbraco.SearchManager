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
import { DiagnoseService, type NodeCandidate, type NodePresenceResult } from "../../api/index.js";
import { editorPath } from "../../explore/editor-link.js";
import { DATE_TIME } from "../../shared/backoffice-floor.js";
import { describeError } from "../../shared/describe-error.js";
import { SearchManagerDiagnoseChangeEvent } from "../diagnose-events.js";
import { cultureStateKey } from "./node-model.js";
import "./node-match.element.js";
import "./node-presence.element.js";

/** How long typing must pause before candidates are looked up. */
const SEARCH_DELAY_MS = 250;

/** How many candidates the list shows. */
const CANDIDATES = 10;

/**
 * The Find a node tool of the Diagnose view: where one document is indexed, and why a query
 * misses it.
 *
 * - **Search:** a name, id or key; candidates from `GET /diagnose/nodes` (debounced) in a
 *   `uui-ref-list`. Picking one sets `node` in the Diagnose URL.
 * - **Node:** with `node` set, `GET /diagnose/nodes/{key}` gives the summary (document type,
 *   published, trashed, protected, cultures with their state) and the presence table
 *   (`search-manager-node-presence`), then the query test (`search-manager-node-match`).
 *
 * Documents only; media are not looked up.
 *
 * Not bound to a manifest; the Diagnose view renders it and keeps `node`, `culture` and `q` in
 * the URL from its events.
 *
 * @element search-manager-find-node
 * @fires search-manager-diagnose-change - {@link SearchManagerDiagnoseChangeEvent} with `node`
 *   when a candidate is picked (the query test fires its own `q` and `culture`).
 */
@customElement("search-manager-find-node")
export class SearchManagerFindNodeElement extends UmbLitElement {
  /** The key of the node shown, from the URL. */
  @property({ attribute: false })
  nodeKey?: string;

  /** The culture of the query test, from the URL. */
  @property({ attribute: false })
  culture?: string;

  /** The query of the query test, from the URL. */
  @property({ attribute: false })
  query?: string;

  /** The text in the search box. */
  @state()
  private _search = "";

  /** The candidates for the search text; undefined when nothing was searched. */
  @state()
  private _candidates?: Array<NodeCandidate>;

  /** The node's presence; undefined while loading or without a node. */
  @state()
  private _presence?: NodePresenceResult;

  /** Whether the node is loading. */
  @state()
  private _loading = false;

  /** Why the node or the candidates could not be loaded, if they could not. */
  @state()
  private _error?: string;

  #searchTimer?: ReturnType<typeof setTimeout>;
  #searchRequest?: AbortController;
  #presenceRequest?: AbortController;

  /**
   * Loads the node again when its key changes.
   *
   * @param changed - The changed properties.
   */
  protected override updated(changed: PropertyValues<this>): void {
    super.updated(changed);
    if (changed.has("nodeKey")) void this.#loadPresence();
  }

  /** Stops pending lookups when the element leaves the page. */
  override disconnectedCallback(): void {
    super.disconnectedCallback();
    clearTimeout(this.#searchTimer);
    this.#searchRequest?.abort();
    this.#presenceRequest?.abort();
  }

  /** Loads where the node is indexed; a 404 means no document has the key. */
  async #loadPresence(): Promise<void> {
    this.#presenceRequest?.abort();
    this._presence = undefined;
    this._error = undefined;
    const key = this.nodeKey;
    if (!key) {
      this._loading = false;
      return;
    }

    const controller = new AbortController();
    this.#presenceRequest = controller;
    this._loading = true;
    const { data, error, response } = await DiagnoseService.getNodePresence({
      path: { key },
      signal: controller.signal,
    }).catch((reason: unknown) => ({ data: undefined, error: reason, response: undefined }));
    if (controller.signal.aborted) return;
    this._loading = false;
    if (data) this._presence = data;
    else
      this._error =
        response?.status === 404
          ? this.localize.term("searchManager_nodeNotFound")
          : this.localize.term("searchManager_nodeLoadError", describeError(error, response));
  }

  /**
   * Looks candidates up once typing pauses.
   *
   * @param value - The search text.
   */
  #onSearch(value: string): void {
    this._search = value;
    clearTimeout(this.#searchTimer);
    this.#searchRequest?.abort();
    if (!value.trim()) {
      this._candidates = undefined;
      return;
    }
    this.#searchTimer = setTimeout(() => void this.#findCandidates(value.trim()), SEARCH_DELAY_MS);
  }

  /**
   * Fetches the candidates for a search text.
   *
   * @param query - A name, id or key.
   */
  async #findCandidates(query: string): Promise<void> {
    const controller = new AbortController();
    this.#searchRequest = controller;
    const { data, error, response } = await DiagnoseService.findNodes({
      query: { query, take: CANDIDATES },
      signal: controller.signal,
    }).catch((reason: unknown) => ({ data: undefined, error: reason, response: undefined }));
    if (controller.signal.aborted) return;
    if (data) this._candidates = data;
    else this._error = this.localize.term("searchManager_nodeSearchError", describeError(error, response));
  }

  /**
   * Shows a candidate: clears the search and writes the node to the URL.
   *
   * @param candidate - The picked node.
   */
  #pick(candidate: NodeCandidate): void {
    this._search = "";
    this._candidates = undefined;
    this.dispatchEvent(new SearchManagerDiagnoseChangeEvent({ node: candidate.key, q: undefined, culture: undefined }));
  }

  /**
   * Renders the search, then the node.
   *
   * @returns The template.
   */
  override render() {
    return html`
      <uui-box headline=${this.localize.term("searchManager_nodeSearchTitle")} headline-variant="h4">
        <uui-input
          class="search"
          label=${this.localize.term("searchManager_nodeSearchLabel")}
          placeholder=${this.localize.term("searchManager_nodeSearchPlaceholder")}
          .value=${this._search}
          @input=${(event: Event) => this.#onSearch(String((event.target as HTMLInputElement).value))}
        ></uui-input>
        ${this.#renderCandidates()}
      </uui-box>
      ${this._error ? html`<p class="error" role="alert">${this._error}</p>` : nothing}
      ${this._loading ? html`<uui-loader-bar></uui-loader-bar>` : nothing}
      ${this._presence ? this.#renderNode(this._presence) : nothing}
    `;
  }

  /**
   * Renders the candidate list.
   *
   * @returns The template.
   */
  #renderCandidates() {
    if (!this._candidates) return nothing;
    if (this._candidates.length === 0) {
      return html`<p class="sub">${this.localize.term("searchManager_nodeNoCandidates")}</p>`;
    }
    // uui-ref-node renders the name as a button, so each candidate is keyboard-reachable; its
    // `open` event fires on click and Enter.
    return html`
      <uui-ref-list class="candidates">
        ${this._candidates.map(
          (candidate) =>
            html`<uui-ref-node
              name=${candidate.name}
              detail=${
                candidate.trashed
                  ? this.localize.term("searchManager_nodeCandidateTrashed", String(candidate.id))
                  : `${candidate.id} · ${candidate.key}`
              }
              @open=${() => this.#pick(candidate)}
            >
              <umb-icon slot="icon" name=${candidate.icon || "icon-document"}></umb-icon>
            </uui-ref-node>`,
        )}
      </uui-ref-list>
    `;
  }

  /**
   * Renders the node summary, the presence table and the query test.
   *
   * @param presence - The node and its presence.
   * @returns The template.
   */
  #renderNode(presence: NodePresenceResult) {
    const node = presence.node;
    const editor = editorPath({ entityType: "content", key: node.key });
    return html`
      <uui-box class="node">
        <div slot="headline" class="headline">
          <h4>${node.name}</h4>
          <uui-tag look="secondary">${node.contentTypeAlias}</uui-tag>
          <uui-tag look="secondary" color=${node.published ? "positive" : "default"}>
            ${this.localize.term(node.published ? "searchManager_nodePublished" : "searchManager_nodeUnpublished")}
          </uui-tag>
          ${node.trashed ? html`<uui-tag color="danger">${this.localize.term("searchManager_nodeTrashed")}</uui-tag>` : nothing}
          ${node.protected ? html`<uui-tag color="warning">${this.localize.term("searchManager_nodeProtected")}</uui-tag>` : nothing}
        </div>
        ${
          editor
            ? html`<uui-button
                slot="header-actions"
                look="secondary"
                compact
                href=${editor}
                label=${this.localize.term("searchManager_documentOpen")}
              ></uui-button>`
            : nothing
        }
        <dl>
          <dt>${this.localize.term("searchManager_columnId")}</dt>
          <dd>${node.id}</dd>
          <dt>${this.localize.term("searchManager_nodeKey")}</dt>
          <dd class="mono">${node.key}</dd>
          <dt>${this.localize.term("searchManager_columnUpdated")}</dt>
          <dd>${this.localize.date(node.updateDate, DATE_TIME)}</dd>
          ${
            node.cultures.length > 0
              ? html`<dt>${this.localize.term("searchManager_columnCultures")}</dt>
                  <dd>
                    <ul class="cultures">
                      ${node.cultures.map(
                        (culture) =>
                          html`<li>
                            <span class="mono">${culture.iso}</span>
                            <span class="sub">${this.localize.term(cultureStateKey(culture))}</span>
                          </li>`,
                      )}
                    </ul>
                  </dd>`
              : nothing
          }
        </dl>
      </uui-box>
      <uui-box headline=${this.localize.term("searchManager_nodePresenceTitle")} headline-variant="h4">
        <search-manager-node-presence .indexes=${presence.indexes}></search-manager-node-presence>
      </uui-box>
      <uui-box headline=${this.localize.term("searchManager_matchTitle")} headline-variant="h4">
        <search-manager-node-match
          .nodeKey=${node.key}
          .indexes=${presence.indexes}
          .cultures=${node.cultures}
          .query=${this.query}
          .culture=${this.culture}
        ></search-manager-node-match>
      </uui-box>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-4);
      }

      .search {
        width: 100%;
      }

      .candidates {
        margin-top: var(--uui-size-space-3);
      }

      .headline {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .headline h4 {
        margin: 0;
      }

      dl {
        display: grid;
        grid-template-columns: minmax(100px, max-content) 1fr;
        gap: var(--uui-size-space-2) var(--uui-size-space-5);
        margin: 0;
      }

      dt {
        font-weight: bold;
      }

      dd {
        margin: 0;
        overflow-wrap: anywhere;
      }

      .cultures {
        list-style: none;
        margin: 0;
        padding: 0;
      }

      .cultures li {
        display: flex;
        gap: var(--uui-size-space-2);
      }

      .sub {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      .mono {
        font-family: monospace;
      }

      .error {
        color: var(--uui-color-danger-standalone);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-find-node": SearchManagerFindNodeElement;
  }
}
