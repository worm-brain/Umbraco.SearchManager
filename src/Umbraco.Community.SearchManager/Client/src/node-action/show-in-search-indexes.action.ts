import { UmbEntityActionBase } from "@umbraco-cms/backoffice/entity-action";
import { umbOpenModal } from "@umbraco-cms/backoffice/modal";
import { SEARCH_MANAGER_NODE_PRESENCE_MODAL } from "./node-presence-modal.token.js";

/**
 * The "Show in search indexes" entity action on documents: opens a sidebar with every index's
 * presence for the document (`args.unique` is its key).
 *
 * Bound by the `Umbraco.Community.SearchManager.EntityAction.ShowInSearchIndexes` manifest.
 * Opens the modal with `umbOpenModal`, as the Indexes view does, rather than consuming
 * `UMB_MODAL_MANAGER_CONTEXT` by hand: the helper does exactly that and exists at the 17.0 floor.
 */
export class SearchManagerShowInSearchIndexesEntityAction extends UmbEntityActionBase<never> {
  /**
   * Opens the sidebar. Closing it rejects the modal promise, which is not an error here.
   *
   * @returns When the sidebar has closed.
   */
  override async execute(): Promise<void> {
    const key = this.args.unique;
    if (!key) return;
    await umbOpenModal(this, SEARCH_MANAGER_NODE_PRESENCE_MODAL, { data: { key } }).catch(() => undefined);
  }
}

export { SearchManagerShowInSearchIndexesEntityAction as api };
