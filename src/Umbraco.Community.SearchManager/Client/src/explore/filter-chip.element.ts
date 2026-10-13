import { css, customElement, html, property, query, state } from "@umbraco-cms/backoffice/external/lit";
import type { UUIPopoverContainerElement } from "@umbraco-cms/backoffice/external/uui";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import type { ExploreSchema, Filter } from "../api/index.js";
import type { SearchManagerChipEditorElement } from "./chip-editor.element.js";
import { describeFilter, type ChipKind } from "./chip-format.js";
import { SearchManagerChipEditCancelEvent, SearchManagerChipRemoveEvent } from "./explore-events.js";
import "./chip-editor.element.js";

/** Tag look and colour per chip kind: include neutral, exclude danger, free text secondary. */
const TAG_STYLE: Readonly<Record<ChipKind, { look: string; color: string }>> = {
  include: { look: "outline", color: "default" },
  exclude: { look: "outline", color: "danger" },
  text: { look: "secondary", color: "default" },
};

/**
 * One chip in the Explore search box: a pill `uui-tag` with the chip's sentence and a remove
 * button. Pressing the sentence opens `search-manager-chip-editor` in a popover.
 *
 * A chip that does not run ({@link disabledReason}) stays in place, drawn struck through at
 * reduced opacity, with the reason as its tooltip and accessible name; it can still be edited or
 * removed. The view does not send chips the target cannot run; chips the server skipped are
 * still sent, so the server's indexes keep pointing at the same chips.
 *
 * Not bound to a manifest; the search box renders one per chip and listens for its events.
 *
 * @element search-manager-filter-chip
 * @fires search-manager-chip-change - From the editor's Apply, with the edited chip.
 * @fires search-manager-chip-remove - {@link SearchManagerChipRemoveEvent} from the remove button.
 */
@customElement("search-manager-filter-chip")
export class SearchManagerFilterChipElement extends UmbLitElement {
  /** The chip. */
  @property({ attribute: false })
  filter?: Filter;

  /** The target's schema, for labels and the editor's operators. */
  @property({ attribute: false })
  schema?: ExploreSchema;

  /** Why the target cannot run the chip; empty when it can. */
  @property({ attribute: false })
  disabledReason = "";

  @state()
  private _open = false;

  @query("#editor")
  private _popover?: UUIPopoverContainerElement;

  @query("search-manager-chip-editor")
  private _editor?: SearchManagerChipEditorElement;

  @query(".label")
  private _label?: HTMLElement;

  #onToggle(event: Event): void {
    this._open = (event as ToggleEvent).newState === "open";
    if (this._open) {
      this._editor?.reset();
      void this._editor?.focusFirstField();
    }
  }

  #close(): void {
    this._popover?.hidePopover();
    this._label?.focus();
  }

  /**
   * Renders the pill and its editor popover.
   *
   * @returns The template.
   */
  override render() {
    if (!this.filter) return html``;
    const term = (key: string, ...args: Array<string>) => this.localize.term(key, ...args);
    const display = describeFilter(this.filter, this.schema, term);
    const style = TAG_STYLE[display.kind];
    const disabled = this.disabledReason.length > 0;
    const description = disabled
      ? `${display.description}. ${this.localize.term("searchManager_chipDisabled", this.disabledReason)}`
      : display.description;

    return html`
      <uui-tag
        class="${display.kind}${disabled ? " disabled" : ""}"
        look=${style.look}
        color=${style.color}
        title=${description}
      >
        <span class="content">
          <button
            class="label"
            type="button"
            popovertarget="editor"
            aria-haspopup="dialog"
            aria-expanded=${this._open ? "true" : "false"}
            aria-label=${this.localize.term("searchManager_chipEdit", description)}
          >
            ${display.label}
          </button>
          <uui-button
            compact
            look="default"
            label=${this.localize.term("searchManager_chipRemove", display.description)}
            @click=${() => this.dispatchEvent(new SearchManagerChipRemoveEvent())}
          >
            <umb-icon name="icon-wrong"></umb-icon>
          </uui-button>
        </span>
      </uui-tag>
      <uui-popover-container id="editor" placement="bottom-start" @toggle=${this.#onToggle}>
        <umb-popover-layout>
          <search-manager-chip-editor
            .filter=${this.filter}
            .schema=${this.schema}
            @search-manager-chip-change=${() => this.#close()}
            @search-manager-chip-edit-cancel=${(event: SearchManagerChipEditCancelEvent) => {
              event.stopPropagation();
              this.#close();
            }}
          ></search-manager-chip-editor>
        </umb-popover-layout>
      </uui-popover-container>
    `;
  }

  static override styles = [
    css`
      :host {
        display: inline-flex;
        flex: none;
        max-width: var(--uui-size-100);
      }

      uui-tag {
        max-width: 100%;
        min-width: 0;
        --uui-tag-padding: 0 0 0 var(--uui-size-space-3);
        --uui-tag-border-radius: var(--uui-size-layout-1);
      }

      /* Not run on this target: still shown, visibly not part of the query. */
      uui-tag.disabled {
        opacity: 0.6;
        border-style: dashed;
      }

      uui-tag.disabled .label {
        text-decoration: line-through;
      }

      uui-tag.text {
        color: var(--uui-color-text);
      }

      .content {
        display: inline-flex;
        align-items: center;
        min-width: 0;
        max-width: 100%;
      }

      .label {
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        font: inherit;
        color: inherit;
        background: none;
        border: none;
        padding: 0;
        cursor: pointer;
      }

      .label:focus-visible {
        outline: 2px solid var(--uui-color-focus);
        border-radius: var(--uui-border-radius);
      }

      uui-button {
        flex: none;
        --uui-button-height: var(--uui-size-6);
        --uui-button-padding-left-factor: 0.5;
        --uui-button-padding-right-factor: 0.5;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-filter-chip": SearchManagerFilterChipElement;
  }
}
