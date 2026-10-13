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
import {
  DiagnoseService,
  type AnalysisResult,
  type AnalysisStage,
  type AnalysisToken,
  type AnalyzerFieldsResult,
  type ExploreTarget,
} from "../../api/index.js";
import type { SelectOption } from "../../shared/backoffice-floor.js";
import { describeError } from "../../shared/describe-error.js";
import { SearchManagerDiagnoseChangeEvent } from "../diagnose-events.js";
import {
  analyzerField,
  analyzerInputProblem,
  fieldDisplay,
  filterFields,
  positionGaps,
  searchSentence,
  tokenLook,
  verdictView,
} from "./analyzer-model.js";

/** Which column of the Analyzer an element is; it decides the URL keys it writes. */
export type AnalyzerSide = "left" | "right";

/**
 * One column of the Analyzer tool: a text run through one field of one index.
 *
 * - **Pickers:** the index (`uui-select`) and the field (`uui-combobox`, filterable, in the
 *   server's order: fields first, then system fields, culture variants, raw copies, segments).
 *   The fields come from `GET /diagnose/{target}/analyzer/fields`; with no field in the URL, or
 *   one the index lacks, the server's default field is used.
 * - **Stages:** `POST /diagnose/{target}/analyzer` gives the analyzer's stages, tokenizer first
 *   and the terms the index stores last. Each stage is a row (`li[data-stage]`) with its name,
 *   what it does, and token chips: removed tokens struck through, changed and added ones marked,
 *   gaps where a stop word was dropped, and on the last stage the terms the search finds
 *   highlighted. Every mark also has visually-hidden words, so nothing relies on colour alone.
 * - **Verdict:** the server's verdict (it ran the engine's real query against the text) in a
 *   `role="status"` strip, with a sentence saying what the search becomes and the engine's notes.
 *
 * Text and search come from the parent, already debounced; a picker change analyses at once.
 * A sequence number per request drops answers that arrive after a newer request was sent.
 * Every value from the server is rendered as text.
 *
 * Not bound to a manifest; `search-manager-analyzer` renders two of them.
 *
 * @element search-manager-analyzer-column
 * @fires search-manager-diagnose-change - {@link SearchManagerDiagnoseChangeEvent} with `left` /
 *   `leftField` (or `right` / `rightField`) when the user picks an index or a field.
 * @cssprop --uui-color-text-alt - Secondary text (details, notes).
 */
@customElement("search-manager-analyzer-column")
export class SearchManagerAnalyzerColumnElement extends UmbLitElement {
  /** Which column this is; reflected as the `side` attribute for tests and styling. */
  @property({ reflect: true })
  side: AnalyzerSide = "left";

  /** The indexes to pick from. */
  @property({ attribute: false })
  targets: ReadonlyArray<ExploreTarget> = [];

  /** The index analysed, already resolved by the parent; changing it loads its fields again. */
  @property({ attribute: false })
  target?: string;

  /** The field the URL names; undefined (or a field the index lacks) uses the default field. */
  @property({ attribute: false })
  field?: string;

  /** The text to index, debounced by the parent. */
  @property({ attribute: false })
  text = "";

  /** The search, debounced by the parent; empty for none. */
  @property({ attribute: false })
  term = "";

  /** The index's analyzable fields; undefined while loading or after an error. */
  @state()
  private _fields?: AnalyzerFieldsResult;

  /** The latest analysis; kept while a newer one loads so the column does not flicker. */
  @state()
  private _result?: AnalysisResult;

  /** Whether an analysis is in flight. */
  @state()
  private _loading = false;

  /** Why the fields or the analysis could not be loaded, if they could not. */
  @state()
  private _error?: string;

  /** The text typed in the field picker, to filter its options. */
  @state()
  private _fieldSearch = "";

  /** Counts field loads; an answer for an older load is dropped. */
  #fieldsSequence = 0;

  /** Counts analyses; an answer for an older analysis is dropped. */
  #analysisSequence = 0;

  /**
   * Loads the fields when the index changes, and analyses again when the field, text or search
   * change.
   *
   * @param changed - The changed properties.
   */
  protected override updated(changed: PropertyValues<this>): void {
    super.updated(changed);
    if (changed.has("target") && changed.get("target") !== this.target) {
      void this.#loadFields();
      return;
    }
    if (changed.has("field") || changed.has("text") || changed.has("term")) void this.#analyze();
  }

  /** Drops answers still in flight when the element leaves the page. */
  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#fieldsSequence++;
    this.#analysisSequence++;
  }

  /** Loads the index's fields, then analyses. */
  async #loadFields(): Promise<void> {
    const sequence = ++this.#fieldsSequence;
    // A pending analysis belongs to the old index.
    this.#analysisSequence++;
    this._fields = undefined;
    this._result = undefined;
    this._error = undefined;
    this._fieldSearch = "";
    const target = this.target;
    if (!target) return;

    this._loading = true;
    const { data, error, response } = await DiagnoseService.getAnalyzerFields({ path: { target } }).catch(
      (reason: unknown) => ({ data: undefined, error: reason, response: undefined }),
    );
    if (sequence !== this.#fieldsSequence) return;
    this._loading = false;
    if (!data) {
      this._error = this.localize.term("searchManager_analyzerFieldsError", describeError(error, response));
      return;
    }
    this._fields = data;
    await this.#analyze();
  }

  /** Analyses the text in the field, unless the input would be refused or there is no field. */
  async #analyze(): Promise<void> {
    const sequence = ++this.#analysisSequence;
    const target = this.target;
    const fields = this._fields;
    const field = fields?.available ? analyzerField(fields, this.field) : undefined;
    // Empty or overlong input is never sent: the parent shows why instead.
    if (!target || !field || analyzerInputProblem(this.text, this.term)) {
      this._result = undefined;
      this._loading = false;
      return;
    }

    this._loading = true;
    const { data, error, response } = await DiagnoseService.analyze({
      path: { target },
      body: { field, text: this.text, searchTerm: this.term.trim() || null },
    }).catch((reason: unknown) => ({ data: undefined, error: reason, response: undefined }));
    if (sequence !== this.#analysisSequence) return;
    this._loading = false;
    if (data) {
      this._result = data;
      this._error = undefined;
    } else {
      this._result = undefined;
      this._error = this.localize.term("searchManager_analyzerError", describeError(error, response));
    }
  }

  /**
   * Writes a picked index to the URL; its field resets to the new index's default.
   *
   * @param target - The index.
   */
  #pickTarget(target: string): void {
    if (!target || target === this.target) return;
    this.dispatchEvent(
      new SearchManagerDiagnoseChangeEvent(
        this.side === "left" ? { left: target, leftField: undefined } : { right: target, rightField: undefined },
      ),
    );
  }

  /**
   * Writes a picked field to the URL. Clearing the picker goes back to the default field.
   *
   * @param field - The field, or empty when the picker was cleared.
   */
  #pickField(field: string): void {
    this._fieldSearch = "";
    const value = field || undefined;
    this.dispatchEvent(
      new SearchManagerDiagnoseChangeEvent(this.side === "left" ? { leftField: value } : { rightField: value }),
    );
  }

  /**
   * Renders the column: a region named after the index, with its pickers and analysis.
   *
   * @returns The template.
   */
  override render() {
    const target = this.target ?? "";
    // The section is a landmark region named after the index, so each column is easy to find.
    return html`
      <section aria-label=${target}>
        <uui-box headline=${target} headline-variant="h4">
          <div class="pickers">${this.#renderIndexPicker()} ${this.#renderFieldPicker()}</div>
          ${this._loading ? html`<uui-loader-bar></uui-loader-bar>` : nothing}
          ${this._error ? html`<p class="error" role="alert">${this._error}</p>` : nothing} ${this.#renderBody()}
        </uui-box>
      </section>
    `;
  }

  /**
   * Renders the index picker.
   *
   * @returns The template.
   */
  #renderIndexPicker() {
    const options: Array<SelectOption> = this.targets.map((candidate) => ({
      name: candidate.name,
      value: candidate.name,
      selected: candidate.name === this.target,
    }));
    return html`
      <div class="picker">
        <uui-label for="index">${this.localize.term("searchManager_analyzerIndex")}</uui-label>
        <uui-select
          id="index"
          label=${this.localize.term("searchManager_analyzerIndex")}
          .options=${options}
          @change=${(event: Event) => this.#pickTarget((event.target as HTMLSelectElement).value)}
        ></uui-select>
      </div>
    `;
  }

  /**
   * Renders the field picker, once the fields are known. `uui-combobox` filters nothing itself:
   * its `search` event gives the typed text and the options are filtered here.
   *
   * @returns The template.
   */
  #renderFieldPicker() {
    const fields = this._fields;
    if (!fields?.available || fields.fields.length === 0) return nothing;
    const current = analyzerField(fields, this.field) ?? "";
    return html`
      <div class="picker field">
        <uui-label for="field">${this.localize.term("searchManager_analyzerField")}</uui-label>
        <uui-combobox
          id="field"
          label=${this.localize.term("searchManager_analyzerField")}
          .value=${current}
          @search=${(event: Event) => (this._fieldSearch = (event.target as HTMLElement & { search: string }).search)}
          @change=${(event: Event) => this.#pickField(String((event.target as HTMLElement & { value: unknown }).value))}
        >
          <uui-combobox-list>
            ${filterFields(fields.fields, this._fieldSearch).map(
              (field) =>
                html`<uui-combobox-list-option .value=${field.name} .displayValue=${fieldDisplay(field)}>
                  <span>${fieldDisplay(field)}</span>
                  ${field.group === "Field" ? nothing : html`<small class="sub">${field.group}</small>`}
                </uui-combobox-list-option>`,
            )}
          </uui-combobox-list>
        </uui-combobox>
      </div>
    `;
  }

  /**
   * Renders why the index or field cannot be analysed, or the analysis.
   *
   * @returns The template.
   */
  #renderBody() {
    const fields = this._fields;
    if (fields && !fields.available) {
      return html`<p class="unavailable">
        ${fields.unavailableReason || this.localize.term("searchManager_analyzerUnavailable")}
      </p>`;
    }
    if (fields && fields.fields.length === 0) {
      return html`<p class="unavailable">${this.localize.term("searchManager_analyzerNoFields")}</p>`;
    }

    const result = this._result;
    if (!result) return nothing;
    if (!result.available) {
      return html`<p class="unavailable">
        ${result.unavailableReason || this.localize.term("searchManager_analyzerUnavailable")}
      </p>`;
    }

    return html`
      <div class="about">
        ${result.analyzer ? html`<div>${this.localize.term("searchManager_analyzerName", result.analyzer)}</div>` : nothing}
        ${result.stagesNote ? html`<div class="sub">${result.stagesNote}</div>` : nothing}
        ${result.truncated ? html`<div class="sub">${this.localize.term("searchManager_analyzerTruncated")}</div>` : nothing}
      </div>
      <ol class="stages" aria-label=${this.localize.term("searchManager_analyzerStagesLabel")}>
        ${result.stages.map((stage, index) => this.#renderStage(stage, index === result.stages.length - 1))}
      </ol>
      ${this.#renderVerdict(result)}
    `;
  }

  /**
   * Renders one stage: its name, what it does, and its tokens. `data-stage` carries the stage
   * name and `data-final` marks the stage whose terms the index stores.
   *
   * @param stage - The stage.
   * @param final - Whether it is the last stage.
   * @returns The template.
   */
  #renderStage(stage: AnalysisStage, final: boolean) {
    const gaps = positionGaps(stage.tokens);
    return html`
      <li class="stage" data-stage=${stage.name} ?data-final=${final}>
        <div class="stage-head">
          <span class="mono stage-name">${stage.name}</span>
          ${stage.detail ? html`<span class="sub">${stage.detail}</span>` : nothing}
          ${final ? html`<uui-tag look="secondary">${this.localize.term("searchManager_analyzerStored")}</uui-tag>` : nothing}
        </div>
        <div class="tokens">
          ${
            stage.tokens.length === 0
              ? html`<span class="sub">${this.localize.term("searchManager_analyzerNoTokens")}</span>`
              : stage.tokens.map((token, index) => this.#renderToken(token, final, gaps[index] ?? 0))
          }
        </div>
      </li>
    `;
  }

  /**
   * Renders one token chip, preceded by a gap marker when positions were skipped before it.
   * Struck, marked and highlighted tokens also carry visually-hidden words saying so.
   *
   * @param token - The token.
   * @param final - Whether the token is in the last stage.
   * @param gap - Positions skipped just before the token.
   * @returns The template.
   */
  #renderToken(token: AnalysisToken, final: boolean, gap: number) {
    const look = tokenLook(token, final);
    const text = look.struck
      ? html`<s>${token.text}</s>`
      : look.marked
        ? html`<mark>${token.text}</mark>`
        : html`<span>${token.text}</span>`;
    const hidden = look.labels.map((key) => this.localize.term(key)).join(", ");
    const gapText = this.localize.term("searchManager_analyzerGap", String(gap));
    return html`${
        gap > 0 ? html`<span class="gap" title=${gapText}><span class="sr-only">${gapText}</span></span>` : nothing
      }<span
        class=${look.className}
        data-state=${token.state}
        data-match=${look.marked ? token.match : "None"}
        title=${this.localize.term(
          "searchManager_analyzerTokenTitle",
          String(token.position),
          String(token.startOffset),
          String(token.endOffset),
        )}
        >${look.marked ? html`<umb-icon name="icon-check"></umb-icon>` : nothing}${text}${
          hidden ? html`<span class="sr-only"> (${hidden})</span>` : nothing
        }</span
      >`;
  }

  /**
   * Renders the verdict strip: a prompt without a search, the reason when this field's searches
   * are not judged, or the server's verdict with what the search becomes and its notes.
   * `data-verdict` is `match`, `no-match`, `prompt` or `unavailable`.
   *
   * @param result - The analysis.
   * @returns The template.
   */
  #renderVerdict(result: AnalysisResult) {
    const view = verdictView(result, this.term);
    if (view.kind === "prompt") {
      return html`<div class="verdict" role="status" data-verdict="prompt">
        <span class="sub">${this.localize.term("searchManager_analyzerNoTerm")}</span>
      </div>`;
    }
    if (view.kind === "unavailable") {
      return html`<div class="verdict" role="status" data-verdict="unavailable">
        <span class="sub">${view.reason}</span>
      </div>`;
    }

    const search = result.search!;
    const sentence = searchSentence(search.parts, (key, ...args) => this.localize.term(key, ...args));
    return html`
      <div class="verdict" role="status" data-verdict=${view.matches ? "match" : "no-match"}>
        <div class="outcome">
          <umb-icon name=${view.look.icon} style=${`color: ${view.look.color}`}></umb-icon>
          <strong>${this.localize.term(view.look.label)}</strong>
        </div>
        ${sentence ? html`<p class="sentence">${sentence}</p>` : nothing}
        ${
          search.notes.length > 0
            ? html`<ul class="notes">
                ${search.notes.map((note) => html`<li>${note}</li>`)}
              </ul>`
            : nothing
        }
      </div>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
        min-width: 0;
      }

      uui-box {
        height: 100%;
      }

      .pickers {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-3) var(--uui-size-space-5);
        margin-bottom: var(--uui-size-space-4);
      }

      .picker {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        min-width: 0;
      }

      .picker.field {
        flex: 1;
      }

      .picker.field uui-combobox {
        flex: 1;
        min-width: 12rem;
      }

      .about {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-1);
        margin-bottom: var(--uui-size-space-4);
      }

      .sub,
      .notes {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      .mono {
        font-family: monospace;
      }

      .stages {
        list-style: none;
        margin: 0;
        padding: 0;
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-4);
      }

      .stage {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
        padding-bottom: var(--uui-size-space-4);
        border-bottom: 1px solid var(--uui-color-divider);
      }

      .stage-head {
        display: flex;
        flex-wrap: wrap;
        align-items: baseline;
        gap: var(--uui-size-space-1) var(--uui-size-space-3);
      }

      .stage-name {
        font-weight: bold;
        overflow-wrap: anywhere;
      }

      .tokens {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-2);
      }

      .token {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-1);
        padding: var(--uui-size-space-1) var(--uui-size-space-2);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface-alt);
        font-family: monospace;
        overflow-wrap: anywhere;
      }

      /* Dropped by this stage: struck through, with a danger outline. */
      .token.removed {
        border-color: var(--uui-color-danger-standalone);
        color: var(--uui-color-text-alt);
        background: transparent;
      }

      /* Rewritten by this stage (lower-cased, stemmed): a subtle marker under the term. */
      .token.changed {
        box-shadow: inset 0 -2px 0 var(--uui-color-interactive-emphasis);
      }

      /* Produced by this stage: a dashed outline. */
      .token.added {
        border-style: dashed;
      }

      /* Found by the search, on the stored terms: the positive colour with a check icon. */
      .token.match {
        border-color: var(--uui-color-positive-standalone);
        background: var(--uui-color-positive);
        color: var(--uui-color-positive-contrast);
      }

      .token mark {
        background: transparent;
        color: inherit;
        font-weight: bold;
      }

      /* An empty slot where a stop word was dropped. */
      .gap {
        display: inline-block;
        align-self: stretch;
        width: var(--uui-size-space-5);
        border: 1px dashed var(--uui-color-border);
        border-radius: var(--uui-border-radius);
      }

      .verdict {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
        margin-top: var(--uui-size-space-4);
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface-alt);
      }

      .outcome {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .sentence {
        margin: 0;
      }

      .notes {
        margin: 0;
        padding-left: var(--uui-size-space-5);
      }

      .unavailable {
        color: var(--uui-color-text-alt);
      }

      .error {
        color: var(--uui-color-danger-standalone);
      }

      .sr-only {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip-path: inset(50%);
        white-space: nowrap;
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-analyzer-column": SearchManagerAnalyzerColumnElement;
  }
}
