import { customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import "../shared/empty-state.element.js";
import { indexFromQuery } from "../umbraco-search/search-manager-links.js";

/**
 * The Diagnose tab, until its first slice lands: says what will be here, for the index a link
 * named with `?index=` (the Umbraco Search box links here) when there is one.
 *
 * Bound by the `Umbraco.Community.SearchManager.WorkspaceView.Diagnose` manifest.
 *
 * @element search-manager-diagnose-view
 */
@customElement("search-manager-diagnose-view")
export class SearchManagerDiagnoseViewElement extends UmbLitElement {
  /**
   * Renders the placeholder.
   *
   * @returns The template.
   */
  override render() {
    const index = indexFromQuery(window.location.search);
    return html`<search-manager-empty-state
      headline=${index ? this.localize.term("searchManager_diagnoseIndex", index) : this.localize.term("searchManager_tabDiagnose")}
    >
      <p>${this.localize.term("searchManager_diagnoseSoon")}</p>
    </search-manager-empty-state>`;
  }
}

export { SearchManagerDiagnoseViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-diagnose-view": SearchManagerDiagnoseViewElement;
  }
}
