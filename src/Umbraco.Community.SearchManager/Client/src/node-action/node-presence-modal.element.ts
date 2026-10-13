import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbModalBaseElement } from "@umbraco-cms/backoffice/modal";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { DiagnoseService, type NodePresenceResult } from "../api/index.js";
import "../diagnose/node/node-presence.element.js";
import { describeError } from "../shared/describe-error.js";
import { findNodeUrl } from "../shared/explore-links.js";
import type { NodePresenceModalData } from "./node-presence-modal.token.js";

/**
 * The "Show in search indexes" sidebar: which indexes hold a document, from
 * `GET /diagnose/nodes/{key}`, in the same presence table as Diagnose's Find a node tool, with a
 * link to that tool for the query test.
 *
 * Bound by the `Umbraco.Community.SearchManager.Modal.NodePresence` manifest; opened by the
 * `Umbraco.Community.SearchManager.EntityAction.ShowInSearchIndexes` entity action.
 *
 * @element search-manager-node-presence-modal
 */
@customElement("search-manager-node-presence-modal")
export class SearchManagerNodePresenceModalElement extends UmbModalBaseElement<NodePresenceModalData, never> {
  /** The document and its presence; undefined while loading. */
  @state()
  private _presence?: NodePresenceResult;

  /** Why the presence could not be loaded, if it could not. */
  @state()
  private _error?: string;

  /** Loads the presence once the sidebar is open. */
  override connectedCallback(): void {
    super.connectedCallback();
    void this.#load();
  }

  /** Fetches the document's presence. */
  async #load(): Promise<void> {
    const key = this.data?.key;
    if (!key) return;
    const { data, error, response } = await DiagnoseService.getNodePresence({ path: { key } });
    if (data) this._presence = data;
    else
      this._error =
        response?.status === 404
          ? this.localize.term("searchManager_nodeNotFound")
          : this.localize.term("searchManager_nodeLoadError", describeError(error, response));
  }

  /**
   * Renders the sidebar.
   *
   * @returns The template.
   */
  override render() {
    const key = this.data?.key;
    return html`
      <umb-body-layout headline=${this._presence?.node.name ?? this.localize.term("searchManager_showInSearchIndexes")}>
        ${this.#renderBody()}
        ${
          key
            ? html`<uui-button
                slot="actions"
                look="secondary"
                href=${findNodeUrl(key)}
                label=${this.localize.term("searchManager_openInSearchManager")}
                @click=${
                  // The link leaves the content section; close the sidebar so it does not stay
                  // open over the Diagnose view. The anchor's navigation still happens.
                  () => this._rejectModal()
                }
              ></uui-button>`
            : nothing
        }
        <uui-button
          slot="actions"
          look="secondary"
          label=${this.localize.term("general_close")}
          @click=${() => this._rejectModal()}
        ></uui-button>
      </umb-body-layout>
    `;
  }

  /**
   * Renders the presence table, a loader or the error.
   *
   * @returns The template.
   */
  #renderBody() {
    if (this._error) return html`<p class="error" role="alert">${this._error}</p>`;
    if (!this._presence) return html`<uui-loader-bar></uui-loader-bar>`;
    return html`
      <uui-box headline=${this.localize.term("searchManager_nodePresenceTitle")}>
        <search-manager-node-presence .indexes=${this._presence.indexes}></search-manager-node-presence>
      </uui-box>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      .error {
        color: var(--uui-color-danger-standalone);
      }
    `,
  ];
}

export { SearchManagerNodePresenceModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-node-presence-modal": SearchManagerNodePresenceModalElement;
  }
}
