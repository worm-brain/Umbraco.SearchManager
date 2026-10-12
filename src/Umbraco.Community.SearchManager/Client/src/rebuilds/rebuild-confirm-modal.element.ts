import { css, customElement, html, nothing } from "@umbraco-cms/backoffice/external/lit";
import { UmbModalBaseElement } from "@umbraco-cms/backoffice/modal";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { RebuildConfirmModalData, RebuildConfirmModalValue } from "./rebuild-confirm-modal.token.js";

/**
 * Confirms a rebuild before it is queued: lists each index with what happens to live search
 * while it rebuilds (core Examine indexes are cleared first; Umbraco Search with zero downtime
 * keeps serving the current copy), and offers one at a time (the default) or all at once.
 *
 * Bound by the `Umbraco.Community.SearchManager.Modal.RebuildConfirm` manifest; opened with
 * `SEARCH_MANAGER_REBUILD_CONFIRM_MODAL`.
 *
 * @element search-manager-rebuild-confirm-modal
 */
@customElement("search-manager-rebuild-confirm-modal")
export class SearchManagerRebuildConfirmModalElement extends UmbModalBaseElement<
  RebuildConfirmModalData,
  RebuildConfirmModalValue
> {
  /**
   * Renders the dialog.
   *
   * @returns The template.
   */
  override render() {
    const indexes = this.data?.indexes ?? [];
    // On a load-balanced site, core Examine rebuilds reach only this server (Umbraco-CMS#8060).
    const server = this.data?.loadBalancedServer;
    const thisServerOnly = !!server && indexes.some((index) => index.engine === "examine");
    const sequential = (this.value?.mode ?? "OneAtATime") === "OneAtATime";
    const headline =
      indexes.length === 1
        ? this.localize.term("searchManager_confirmTitleOne", indexes[0]!.name)
        : this.localize.term("searchManager_confirmTitleMany", indexes.length);

    return html`
      <uui-dialog-layout headline=${headline}>
        <p class="intro">${this.localize.term("searchManager_confirmIntro")}</p>
        <ul>
          ${indexes.map(
            (index) => html`
              <li>
                <strong>${index.name}</strong>
                <span class=${index.zeroDowntime ? "safe" : "cleared"}>
                  ${this.localize.term(
                    index.zeroDowntime ? "searchManager_confirmZeroDowntime" : "searchManager_confirmClearedFirst",
                  )}
                </span>
              </li>
            `,
          )}
        </ul>
        ${
          thisServerOnly
            ? html`<p class="server" role="note">
                <umb-icon name="icon-alert"></umb-icon>
                ${this.localize.term("searchManager_confirmThisServerOnly", server!)}
              </p>`
            : nothing
        }
        <uui-checkbox
          label=${this.localize.term("searchManager_confirmOneAtATime")}
          ?checked=${sequential}
          @change=${(event: Event) =>
            this.updateValue({
              mode: (event.target as HTMLInputElement).checked ? "OneAtATime" : "AllAtOnce",
            })}
        ></uui-checkbox>
        <uui-button
          slot="actions"
          label=${this.localize.term("general_cancel")}
          @click=${() => this._rejectModal()}
        ></uui-button>
        <uui-button
          slot="actions"
          look="primary"
          color="danger"
          label=${
            indexes.length === 1
              ? this.localize.term("searchManager_confirmButtonOne")
              : this.localize.term("searchManager_confirmButtonMany", indexes.length)
          }
          @click=${() => this._submitModal()}
        ></uui-button>
      </uui-dialog-layout>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      uui-dialog-layout {
        max-width: 620px;
      }

      .intro {
        margin-top: 0;
        color: var(--uui-color-text-alt);
      }

      ul {
        list-style: none;
        margin: 0 0 var(--uui-size-space-5);
        padding: 0;
      }

      li {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-1);
        padding: var(--uui-size-space-3) 0;
        border-top: 1px solid var(--uui-color-divider);
      }

      .server {
        display: flex;
        gap: var(--uui-size-space-3);
        align-items: flex-start;
        color: var(--uui-color-warning-contrast);
        background: var(--uui-color-warning);
        padding: var(--uui-size-space-4);
        border-radius: var(--uui-border-radius);
      }

      .cleared {
        color: var(--uui-color-danger-standalone);
      }

      .safe {
        color: var(--uui-color-positive-standalone);
      }
    `,
  ];
}

export { SearchManagerRebuildConfirmModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-rebuild-confirm-modal": SearchManagerRebuildConfirmModalElement;
  }
}
