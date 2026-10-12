import { customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import "../shared/empty-state.element.js";

/**
 * The Explore tab, until its first slice lands: says what will be here.
 *
 * Bound by the `Umbraco.Community.SearchManager.WorkspaceView.Explore` manifest.
 *
 * @element search-manager-explore-view
 */
@customElement("search-manager-explore-view")
export class SearchManagerExploreViewElement extends UmbLitElement {
  /**
   * Renders the placeholder.
   *
   * @returns The template.
   */
  override render() {
    return html`<search-manager-empty-state headline=${this.localize.term("searchManager_tabExplore")}>
      <p>${this.localize.term("searchManager_exploreSoon")}</p>
    </search-manager-empty-state>`;
  }
}

export { SearchManagerExploreViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-explore-view": SearchManagerExploreViewElement;
  }
}
