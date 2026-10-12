import { UmbModalToken } from "@umbraco-cms/backoffice/modal";
import type { RebuildMode } from "../api/types.gen.js";

/** Alias of the rebuild confirmation modal. */
export const SEARCH_MANAGER_REBUILD_CONFIRM_MODAL_ALIAS = "Umbraco.Community.SearchManager.Modal.RebuildConfirm";

/** One index the confirmation lists. */
export interface RebuildConfirmIndex {
  /** The index name. */
  name: string;
  /** Whether search keeps working while it rebuilds. */
  zeroDowntime: boolean;
}

/** What the confirmation shows. */
export interface RebuildConfirmModalData {
  /** The indexes about to be rebuilt. */
  indexes: RebuildConfirmIndex[];
}

/** What the user chose. */
export interface RebuildConfirmModalValue {
  /** One at a time (default) or all at once. */
  mode: RebuildMode;
}

/** Opens the rebuild confirmation; resolves with the chosen mode, rejects when cancelled. */
export const SEARCH_MANAGER_REBUILD_CONFIRM_MODAL = new UmbModalToken<
  RebuildConfirmModalData,
  RebuildConfirmModalValue
>(SEARCH_MANAGER_REBUILD_CONFIRM_MODAL_ALIAS, {
  modal: { type: "dialog" },
  value: { mode: "OneAtATime" },
});
