import {
  css,
  customElement,
  html,
  nothing,
  property,
  query,
  state,
  type PropertyValues,
} from "@umbraco-cms/backoffice/external/lit";
import type { UUIInputElement } from "@umbraco-cms/backoffice/external/uui";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { ExploreService, type ExploreTarget, type NativeWarning } from "../api/index.js";
import { applyFix, markPosition, NativeValidator, type NativeCheck } from "./native-mode.js";

/**
 * The native-mode box: raw Lucene in a monospace `uui-input`, checked as the user types with
 * `POST /explore/{target}/validate` (the parser Examine's `NativeQuery` uses). An invalid query
 * is marked invalid, with the parser's message and the offending character highlighted, and is
 * not run. Warnings (a number or date term without a range, a lower-case operator, an unknown
 * field, a leading wildcard) list under the box with a one-click fix where there is one.
 *
 * Enter (or Run) runs the text once it is known to be valid. Not bound to a manifest; the
 * Explore view renders it in place of the search box.
 *
 * @element search-manager-native-box
 * @fires search-manager-native-run - `CustomEvent<string>` with the query to run.
 */
@customElement("search-manager-native-box")
export class SearchManagerNativeBoxElement extends UmbLitElement {
  /** The target, which must take raw Lucene. */
  @property({ attribute: false })
  target?: ExploreTarget;

  /** The query that ran last; setting a different one replaces the text. */
  @property({ attribute: false })
  query = "";

  @state()
  private _text = "";

  @state()
  private _check: NativeCheck = { status: "idle" };

  @query("#native")
  private _input?: UUIInputElement;

  #validator = new NativeValidator(
    (text, signal) =>
      ExploreService.validateNativeQuery({ path: { target: this.target?.name ?? "" }, body: { query: text }, signal }),
    (check) => (this._check = check),
  );

  /** Takes a new committed query into the box and checks it at once. */
  protected override willUpdate(changed: PropertyValues<this>): void {
    if (changed.has("query") && this.query !== this._text) {
      this._text = this.query;
      void this.#validator.check(this._text, true);
    }
  }

  /** Mirrors the verdict onto the input, so it draws its invalid state. */
  protected override updated(changed: PropertyValues): void {
    super.updated(changed);
    if (changed.has("_check") && this._input) {
      const invalid = this._check.status === "result" && !this._check.result.valid;
      this._input.setCustomValidity(invalid ? this.#errorText() : "");
      if (invalid) this._input.pristine = false;
    }
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#validator.cancel();
  }

  /** Moves focus to the input. */
  override focus(): void {
    void this._input?.focus();
  }

  #setText(text: string, immediately = false): void {
    this._text = text;
    if (this._input) this._input.value = text;
    void this.#validator.check(text, immediately);
  }

  /** Runs the text once a check says it parses; an invalid query stays in the box. */
  async #run(): Promise<void> {
    if (this._check.status !== "result" && this._text.trim().length > 0) await this.#validator.check(this._text, true);
    if (this._check.status === "result" && !this._check.result.valid) return;
    this.dispatchEvent(
      new CustomEvent("search-manager-native-run", { detail: this._text, bubbles: true, composed: true }),
    );
  }

  #errorText(): string {
    if (this._check.status !== "result" || this._check.result.valid) return "";
    const { error, position } = this._check.result;
    return position == null
      ? this.localize.term("searchManager_nativeInvalid", error ?? "")
      : this.localize.term("searchManager_nativeInvalidAt", error ?? "", String(position + 1));
  }

  /**
   * Renders the input, the parser's verdict and the warnings.
   *
   * @returns The template.
   */
  override render() {
    return html`
      <div class="row">
        <uui-input
          id="native"
          label=${this.localize.term("searchManager_nativeLabel")}
          placeholder=${this.localize.term("searchManager_nativePlaceholder")}
          autocomplete="off"
          spellcheck="false"
          .value=${this._text}
          @input=${(event: Event) => this.#setText(String((event.target as UUIInputElement).value ?? ""))}
          @keydown=${(event: KeyboardEvent) => {
            if (event.key !== "Enter" || event.isComposing) return;
            event.preventDefault();
            void this.#run();
          }}
        >
          <uui-tag slot="prepend" look="primary" class="language">Lucene</uui-tag>
        </uui-input>
        <uui-button
          look="primary"
          color="positive"
          label=${this.localize.term("searchManager_nativeRun")}
          @click=${() => void this.#run()}
        ></uui-button>
      </div>
      ${this.#renderCheck()}
    `;
  }

  #renderCheck() {
    const check = this._check;
    if (check.status === "error") {
      return html`<p class="message">${this.localize.term("searchManager_nativeCheckFailed", check.message)}</p>`;
    }
    if (check.status !== "result") return nothing;

    const { valid, position, warnings } = check.result;
    const marked = position == null ? undefined : markPosition(this._text, position);
    return html`
      ${
        valid
          ? nothing
          : html`<div class="message invalid" role="alert">
              <strong>${this.localize.term("searchManager_nativeCannotParse")}</strong>
              ${
                marked
                  ? html`<code class="snippet">${marked.before}<mark>${marked.at}</mark>${marked.after}</code>`
                  : nothing
              }
              <span>${this.#errorText()}</span>
            </div>`
      }
      ${warnings.map((warning) => this.#renderWarning(warning))}
    `;
  }

  #renderWarning(warning: NativeWarning) {
    return html`
      <div class="message warning">
        <umb-icon name="icon-alert" aria-hidden="true"></umb-icon>
        <span class="text">${warning.message}</span>
        ${
          warning.replacement != null
            ? html`<uui-button
                look="secondary"
                compact
                label=${this.localize.term("searchManager_nativeFix", warning.replacement)}
                @click=${() => {
                  this.#setText(applyFix(this._text, warning), true);
                  this.focus();
                }}
              ></uui-button>`
            : nothing
        }
      </div>
    `;
  }

  static override styles = [
    css`
      :host {
        display: block;
        min-width: 0;
      }

      .row {
        display: flex;
        gap: var(--uui-size-space-2);
      }

      uui-input {
        flex: 1;
        min-width: 0;
        /* No UUI monospace token exists; umb-code-block uses the generic family too. */
        font-family: monospace;
      }

      .language {
        margin-left: var(--uui-size-space-2);
        align-self: center;
      }

      .message {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-2);
        margin: var(--uui-size-space-2) 0 0;
        padding: var(--uui-size-space-2) var(--uui-size-space-3);
        border-radius: var(--uui-border-radius);
        border: 1px solid var(--uui-color-border);
        font-size: var(--uui-type-small-size);
      }

      .message.invalid {
        flex-direction: column;
        align-items: flex-start;
        border-color: var(--uui-color-invalid);
        color: var(--uui-color-invalid-standalone);
      }

      .message.warning {
        border-color: var(--uui-color-warning);
        background: var(--uui-color-warning-surface, var(--uui-color-surface));
      }

      .message.warning .text {
        flex: 1 1 16rem;
      }

      .snippet {
        font-family: monospace;
        white-space: pre;
        overflow-x: auto;
        max-width: 100%;
        color: var(--uui-color-text);
      }

      .snippet mark {
        background: var(--uui-color-invalid);
        color: var(--uui-color-invalid-contrast);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-native-box": SearchManagerNativeBoxElement;
  }
}
