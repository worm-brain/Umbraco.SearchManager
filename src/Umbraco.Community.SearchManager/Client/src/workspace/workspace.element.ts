import { css, customElement, html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";

/**
 * The Search Manager workspace shell.
 *
 * `umb-workspace-editor` draws the standard workspace header with the "Search Manager" headline
 * and the Indexes, Explore and Diagnose tabs, which it builds from the `workspaceView` manifests
 * that match this workspace's alias, and routes between them. There is no footer, so
 * `enforceNoFooter` stops the editor reserving one.
 *
 * Bound by the `Umbraco.Community.SearchManager.Workspace` manifest (default workspace kind).
 *
 * @element search-manager-workspace
 */
@customElement("search-manager-workspace")
export class SearchManagerWorkspaceElement extends UmbLitElement {
  /**
   * Renders the workspace editor.
   *
   * @returns The template.
   */
  override render() {
    return html`<umb-workspace-editor
      headline=${this.localize.term("searchManager_title")}
      .enforceNoFooter=${true}
    ></umb-workspace-editor>`;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
        width: 100%;
        height: 100%;
      }
    `,
  ];
}

export { SearchManagerWorkspaceElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-workspace": SearchManagerWorkspaceElement;
  }
}
