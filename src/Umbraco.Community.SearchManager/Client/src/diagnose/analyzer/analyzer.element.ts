import {
  css,
  customElement,
  html,
  nothing,
  property,
  state,
  type PropertyValues,
} from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { ExploreTarget } from "../../api/index.js";
import { SearchManagerDiagnoseChangeEvent } from "../diagnose-events.js";
import "./analyzer-column.element.js";
import { analyzerInputProblem, analyzerSides } from "./analyzer-model.js";

/** How long typing must pause before the text and search are analysed (and written to the URL). */
const ANALYZE_DELAY_MS = 300;

/**
 * The Analyzer tool of the Diagnose view: how two indexes turn the same text into the terms they
 * store, stage by stage, and whether a visitor's search finds it in each.
 *
 * - **Inputs:** "Text to index" (`uui-input#text`) and "A visitor searches for"
 *   (`uui-input#term`). 300 ms after typing stops they are written to the URL (`text`, `term`),
 *   which passes them back to both columns, so the URL is the debounce. Empty text shows a prompt
 *   and overlong input says why, instead of sending a request the server would refuse.
 * - **Columns:** two `search-manager-analyzer-column`s side by side (one above the other on narrow
 *   widths), each with its own index and field pickers. Defaults: ExternalIndex on the left,
 *   InternalIndex on the right, otherwise the first two indexes.
 *
 * Not bound to a manifest; the Diagnose view renders it with the Index targets of both engines
 * (member indexes already filtered by the server) and keeps `text`, `term`, `left`, `leftField`,
 * `right` and `rightField` in the URL from its events.
 *
 * @element search-manager-analyzer
 * @fires search-manager-diagnose-change - {@link SearchManagerDiagnoseChangeEvent} with `text`
 *   and `term` once typing pauses (the columns fire their own index and field changes).
 */
@customElement("search-manager-analyzer")
export class SearchManagerAnalyzerElement extends UmbLitElement {
  /** The indexes to pick from. */
  @property({ attribute: false })
  targets: ReadonlyArray<ExploreTarget> = [];

  /** The text to index, from the URL. */
  @property({ attribute: false })
  text?: string;

  /** The search, from the URL. */
  @property({ attribute: false })
  term?: string;

  /** The left column's index, from the URL; undefined picks the default. */
  @property({ attribute: false })
  left?: string;

  /** The left column's field, from the URL; undefined picks the index's default field. */
  @property({ attribute: false })
  leftField?: string;

  /** The right column's index, from the URL; undefined picks the default. */
  @property({ attribute: false })
  right?: string;

  /** The right column's field, from the URL; undefined picks the index's default field. */
  @property({ attribute: false })
  rightField?: string;

  /** The text as typed, ahead of the URL while typing. */
  @state()
  private _text = "";

  /** The search as typed, ahead of the URL while typing. */
  @state()
  private _term = "";

  /** Whether the inputs were filled from the URL yet. */
  #initialised = false;

  #timer?: ReturnType<typeof setTimeout>;

  /**
   * Fills the inputs from the URL once. Afterwards the URL only changes through this element, so
   * the typed values stay the source of truth and a late round trip cannot overwrite typing.
   *
   * @param changed - The changed properties.
   */
  protected override willUpdate(changed: PropertyValues<this>): void {
    super.willUpdate(changed);
    if (this.#initialised) return;
    this.#initialised = true;
    this._text = this.text ?? "";
    this._term = this.term ?? "";
  }

  /** Stops a pending write when the element leaves the page. */
  override disconnectedCallback(): void {
    super.disconnectedCallback();
    clearTimeout(this.#timer);
  }

  /** Writes the typed text and search to the URL once typing pauses; the columns then analyse. */
  #schedule(): void {
    clearTimeout(this.#timer);
    this.#timer = setTimeout(() => {
      this.dispatchEvent(
        new SearchManagerDiagnoseChangeEvent({ text: this._text || undefined, term: this._term || undefined }),
      );
    }, ANALYZE_DELAY_MS);
  }

  /**
   * Renders the inputs, the input message, and the two columns.
   *
   * @returns The template.
   */
  override render() {
    const sides = analyzerSides(
      this.targets.map((target) => target.name),
      { left: this.left, right: this.right },
    );
    const problem = analyzerInputProblem(this._text, this._term);
    return html`
      <uui-box>
        <p class="intro">${this.localize.term("searchManager_analyzerIntro")}</p>
        <div class="inputs">
          <div class="input">
            <uui-label for="text">${this.localize.term("searchManager_analyzerTextLabel")}</uui-label>
            <uui-input
              id="text"
              label=${this.localize.term("searchManager_analyzerTextLabel")}
              placeholder=${this.localize.term("searchManager_analyzerTextPlaceholder")}
              .value=${this._text}
              @input=${(event: Event) => {
                this._text = String((event.target as HTMLInputElement).value);
                this.#schedule();
              }}
            ></uui-input>
          </div>
          <div class="input">
            <uui-label for="term">${this.localize.term("searchManager_analyzerTermLabel")}</uui-label>
            <uui-input
              id="term"
              label=${this.localize.term("searchManager_analyzerTermLabel")}
              placeholder=${this.localize.term("searchManager_analyzerTermPlaceholder")}
              .value=${this._term}
              @input=${(event: Event) => {
                this._term = String((event.target as HTMLInputElement).value);
                this.#schedule();
              }}
            ></uui-input>
          </div>
        </div>
        ${
          problem
            ? html`<p
                class=${problem[0] === "searchManager_analyzerNoText" ? "prompt" : "error"}
                data-testid="analyzer-message"
              >
                ${this.localize.term(...problem)}
              </p>`
            : nothing
        }
      </uui-box>
      <div class="columns">
        ${this.#renderColumn("left", sides.left, this.leftField)}
        ${this.#renderColumn("right", sides.right, this.rightField)}
      </div>
    `;
  }

  /**
   * Renders one column. `data-index` carries its index name for tests.
   *
   * @param side - Which column.
   * @param target - Its index.
   * @param field - Its field from the URL.
   * @returns The template.
   */
  #renderColumn(side: "left" | "right", target: string | undefined, field: string | undefined) {
    if (!target) return nothing;
    return html`<search-manager-analyzer-column
      side=${side}
      data-index=${target}
      .targets=${this.targets}
      .target=${target}
      .field=${field}
      .text=${this.text ?? ""}
      .term=${this.term ?? ""}
    ></search-manager-analyzer-column>`;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-4);
      }

      .intro {
        margin-top: 0;
        color: var(--uui-color-text-alt);
      }

      .inputs {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(min(100%, 20rem), 1fr));
        gap: var(--uui-size-space-4) var(--uui-size-space-5);
      }

      .input {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-1);
      }

      .input uui-input {
        width: 100%;
      }

      .prompt {
        margin-bottom: 0;
        color: var(--uui-color-text-alt);
      }

      .error {
        margin-bottom: 0;
        color: var(--uui-color-danger-standalone);
      }

      /* Two columns side by side; one above the other once each would be narrower than 22rem. */
      .columns {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(min(100%, 22rem), 1fr));
        gap: var(--uui-size-space-4);
        align-items: start;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-analyzer": SearchManagerAnalyzerElement;
  }
}
