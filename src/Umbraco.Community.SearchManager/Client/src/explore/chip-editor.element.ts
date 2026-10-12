import {
  css,
  customElement,
  html,
  nothing,
  property,
  state,
  type PropertyValues,
} from "@umbraco-cms/backoffice/external/lit";
import type { UUISelectOption } from "@umbraco-cms/backoffice/external/uui";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { ExploreSchema, Filter, FilterOperator } from "../api/index.js";
import {
  draftFromFilter,
  editorOperators,
  filterFromDraft,
  isDraftValid,
  valuePlaceholderKey,
  type ChipDraft,
} from "./chip-edit.js";
import { SearchManagerChipChangeEvent, SearchManagerChipEditCancelEvent } from "./explore-events.js";
import { fieldLabel } from "./filters.js";

/**
 * The form a chip opens to change its operator, value and exclusion. Operators follow the field
 * type the target's schema gives (Text, Keyword, Number, Date, plus "is missing"); free text
 * offers contains or phrase. Enter in the value saves.
 *
 * The editor only reports; its host closes the popover and the search box changes the chips.
 * Not bound to a manifest; `search-manager-filter-chip` and the Explore view render it.
 *
 * @element search-manager-chip-editor
 * @fires search-manager-chip-change - {@link SearchManagerChipChangeEvent} on Apply.
 * @fires search-manager-chip-edit-cancel - {@link SearchManagerChipEditCancelEvent} on Cancel.
 */
@customElement("search-manager-chip-editor")
export class SearchManagerChipEditorElement extends UmbLitElement {
  /** The chip being edited; setting it resets the form. */
  @property({ attribute: false })
  filter?: Filter;

  /** The target's schema, for operators, labels and value hints. */
  @property({ attribute: false })
  schema?: ExploreSchema;

  /** Whether the chip is new (the button reads "Add filter" rather than "Apply"). */
  @property({ type: Boolean, attribute: "is-new" })
  isNew = false;

  @state()
  private _draft?: ChipDraft;

  /** Resets the form when a different chip arrives. */
  override willUpdate(changed: PropertyValues<this>): void {
    if (changed.has("filter")) this.reset();
  }

  /** Puts the form back to the chip's values, for a popover that is reopened. */
  reset(): void {
    this._draft = this.filter ? draftFromFilter(this.filter) : undefined;
  }

  /** Moves focus to the first field, for when the popover opens. */
  async focusFirstField(): Promise<void> {
    await this.updateComplete;
    (this.shadowRoot?.querySelector("[data-first]") as HTMLElement | null)?.focus();
  }

  #save(): void {
    if (!this._draft || !isDraftValid(this._draft)) return;
    this.dispatchEvent(new SearchManagerChipChangeEvent(filterFromDraft(this._draft)));
  }

  #patch(partial: Partial<ChipDraft>): void {
    if (this._draft) this._draft = { ...this._draft, ...partial };
  }

  #onKeydown(event: KeyboardEvent): void {
    if (event.key !== "Enter" || event.isComposing) return;
    event.preventDefault();
    this.#save();
  }

  /**
   * Renders the form.
   *
   * @returns The template.
   */
  override render() {
    const draft = this._draft;
    if (!draft) return nothing;
    const title =
      draft.kind === "Field"
        ? fieldLabel(this.schema, draft.field ?? "")
        : this.localize.term(draft.kind === "Text" ? "searchManager_fieldText" : "searchManager_fieldLucene");
    const operators = editorOperators(draft, this.schema);
    const options: Array<UUISelectOption> = operators.map((op) => ({
      name: this.localize.term(`searchManager_op${op}`),
      value: op,
      selected: op === draft.op,
    }));

    return html`
      <div class="form">
        <strong class="field"
          >${this.localize.term(this.isNew ? "searchManager_editorNewTitle" : "searchManager_editorTitle", title)}</strong
        >
        ${
          operators.length > 0
            ? html`
                <uui-label for="op">${this.localize.term("searchManager_editorOperator")}</uui-label>
                <uui-select
                  id="op"
                  data-first
                  label=${this.localize.term("searchManager_editorOperator")}
                  .options=${options}
                  @change=${(event: Event) => this.#patch({ op: (event.target as HTMLSelectElement).value as FilterOperator })}
                ></uui-select>
              `
            : nothing
        }
        ${
          draft.op === "Missing"
            ? nothing
            : html`
                <uui-label for="value">${this.localize.term("searchManager_editorValue")}</uui-label>
                <uui-input
                  id="value"
                  ?data-first=${operators.length === 0}
                  label=${this.localize.term("searchManager_editorValue")}
                  placeholder=${this.localize.term(valuePlaceholderKey(draft, this.schema))}
                  .value=${draft.value}
                  @input=${(event: Event) => this.#patch({ value: String((event.target as HTMLInputElement).value) })}
                  @keydown=${this.#onKeydown}
                ></uui-input>
              `
        }
        <uui-toggle
          label=${this.localize.term("searchManager_editorExclude")}
          .checked=${draft.exclude}
          @change=${(event: Event) => this.#patch({ exclude: (event.target as HTMLInputElement).checked })}
        ></uui-toggle>
        <div class="actions">
          <uui-button
            look="secondary"
            label=${this.localize.term("searchManager_editorCancel")}
            @click=${() => this.dispatchEvent(new SearchManagerChipEditCancelEvent())}
          ></uui-button>
          <uui-button
            look="primary"
            label=${this.localize.term(this.isNew ? "searchManager_editorAdd" : "searchManager_editorApply")}
            ?disabled=${!isDraftValid(draft)}
            @click=${this.#save}
          ></uui-button>
        </div>
      </div>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      .form {
        display: grid;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-4);
        min-width: var(--uui-size-80);
      }

      .field {
        overflow-wrap: anywhere;
      }

      .actions {
        display: flex;
        justify-content: flex-end;
        gap: var(--uui-size-space-2);
        margin-top: var(--uui-size-space-2);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-chip-editor": SearchManagerChipEditorElement;
  }
}
