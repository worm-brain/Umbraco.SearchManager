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
  ExploreService,
  type ExploreTarget,
  type FieldStatistic,
  type FieldStatisticsResult,
  type UnsupportedFilter,
} from "../../api/index.js";
import { coverage } from "../../explore/suggestions.js";
import { formatCount } from "../../indexes/index-format.js";
import { describeError } from "../../shared/describe-error.js";
import "../../shared/empty-state.element.js";
import { analyzerUrl, exploreUrl } from "../../shared/explore-links.js";
import { SearchManagerDiagnoseChangeEvent } from "../diagnose-events.js";
import {
  canFilterOn,
  coveragePercent,
  missingFilter,
  missingUnsupportedReason,
  termFilter,
  visibleFields,
} from "./field-statistics-model.js";

/**
 * The Fields tool of the Diagnose view: per-field statistics of one index from
 * `GET /diagnose/{target}/fields`, in a `uui-table`:
 *
 * - **Field:** the name a chip takes, its label (or culture, or group), and the physical Lucene
 *   name when it differs (Umbraco Search's `Field_title_texts`).
 * - **Type, analyzer** (a link into the Analyzer tool on this field), **coverage** (share of live
 *   documents with a value, as a `uui-progress-bar`),
 *   **unique terms** ("at least N" when the server sampled).
 * - **Top terms:** each opens Explore with a chip on the field (`Contains` for text, `Is` for
 *   keywords, `Equals` for numbers). Date fields show their range instead. Text fields say their
 *   terms are analysed words, not whole values.
 * - **Missing:** opens Explore with an "is missing" chip, unless the engine has no such filter
 *   (Umbraco Search): then the count shows unlinked, with the engine's reason.
 *
 * `Field` rows always show; system fields, culture variants, raw copies and segments only with
 * the checkbox on. Links into Explore pass `exploreCulture` when the server sets it, since
 * Umbraco Search without a culture finds only invariant documents. Every value from the index is
 * rendered as text.
 *
 * Not bound to a manifest; the Diagnose view renders it and keeps `variants` and `filter` in the
 * URL from its events.
 *
 * @element search-manager-field-statistics
 * @fires search-manager-diagnose-change - {@link SearchManagerDiagnoseChangeEvent} with `filter`
 *   or `variants` when the user changes them.
 * @cssprop --uui-color-text-alt - Secondary text (labels, physical names, notes).
 */
@customElement("search-manager-field-statistics")
export class SearchManagerFieldStatisticsElement extends UmbLitElement {
  /** The index whose fields are shown; changing it loads them again. */
  @property({ attribute: false })
  target?: ExploreTarget;

  /** Whether system fields, culture variants, raw copies and segments show. */
  @property({ type: Boolean })
  variants = false;

  /** The field-name filter text. */
  @property()
  filter = "";

  /** The server's answer for {@link target}; undefined while loading or after an error. */
  @state()
  private _result?: FieldStatisticsResult;

  /** The target schema's engine rules, to tell whether "is missing" chips run. */
  @state()
  private _rules: ReadonlyArray<UnsupportedFilter> = [];

  /** Whether a request is in flight. */
  @state()
  private _loading = false;

  /** Why the statistics could not be loaded, if they could not. */
  @state()
  private _error?: string;

  #request?: AbortController;

  /**
   * Loads the statistics again when the target changes.
   *
   * @param changed - The changed properties.
   */
  protected override updated(changed: PropertyValues<this>): void {
    super.updated(changed);
    if (changed.has("target") && changed.get("target")?.name !== this.target?.name) void this.#load();
  }

  /** Stops a request in flight when the element leaves the page. */
  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.#request?.abort();
  }

  /**
   * Fetches the statistics and the target's schema (for its engine rules) side by side. A newer
   * target aborts the requests for the older one. The schema is optional: without it the missing
   * counts link, and Explore shows a chip it cannot run as disabled with the reason.
   */
  async #load(): Promise<void> {
    this.#request?.abort();
    this._result = undefined;
    this._error = undefined;
    this._rules = [];
    const target = this.target;
    if (!target) return;

    const controller = new AbortController();
    this.#request = controller;
    this._loading = true;
    const failed = (reason: unknown) => ({ data: undefined, error: reason, response: undefined });
    const [statistics, schema] = await Promise.all([
      DiagnoseService.getFieldStatistics({ path: { target: target.name }, signal: controller.signal }).catch(failed),
      ExploreService.getExploreSchema({ path: { target: target.name }, signal: controller.signal }).catch(failed),
    ]);
    if (controller.signal.aborted) return;

    this._loading = false;
    this._rules = schema.data?.unsupported ?? [];
    if (statistics.data) this._result = statistics.data;
    else
      this._error = this.localize.term(
        "searchManager_fieldStatsError",
        describeError(statistics.error, statistics.response),
      );
  }

  /**
   * Renders the loader, the error, the unavailable reason, or the summary and the table.
   *
   * @returns The template.
   */
  override render() {
    const name = this.target?.name ?? "";
    const headline = this.localize.term("searchManager_fieldStatsHeadline", name);
    if (this._error) return html`<p class="error" role="alert">${this._error}</p>`;
    if (this._loading || !this._result) {
      return html`<uui-box headline=${headline} headline-variant="h4">
        <div class="loading" role="status">
          <uui-loader></uui-loader>
          <span>${this.localize.term("searchManager_fieldStatsLoading")}</span>
        </div>
      </uui-box>`;
    }

    const result = this._result;
    if (!result.available) {
      return html`<search-manager-empty-state headline=${headline}>
        <p>${result.unavailableReason ?? this.localize.term("searchManager_fieldStatsUnavailable")}</p>
      </search-manager-empty-state>`;
    }

    const rows = visibleFields(result.fields, { variants: this.variants, filter: this.filter });
    return html`
      <uui-box>
        <div slot="headline" class="headline">
          <h4>${headline}</h4>
          <small class="summary">${this.#summary(result)}</small>
        </div>
        <div slot="header-actions" class="controls">
          <uui-input
            label=${this.localize.term("searchManager_fieldsFilter")}
            placeholder=${this.localize.term("searchManager_fieldsFilter")}
            .value=${this.filter}
            @input=${(event: Event) =>
              this.dispatchEvent(
                new SearchManagerDiagnoseChangeEvent({
                  filter: String((event.target as HTMLInputElement).value) || undefined,
                }),
              )}
          ></uui-input>
          <uui-checkbox
            label=${this.localize.term("searchManager_fieldStatsShowAll")}
            ?checked=${this.variants}
            @change=${(event: Event) =>
              this.dispatchEvent(
                new SearchManagerDiagnoseChangeEvent({ variants: (event.target as HTMLInputElement).checked }),
              )}
          ></uui-checkbox>
        </div>
        ${
          rows.length === 0
            ? html`<p class="sub">${this.localize.term("searchManager_fieldStatsNoFields")}</p>`
            : this.#renderTable(rows, result)
        }
      </uui-box>
    `;
  }

  /**
   * The line under the headline: documents, deleted documents, and the notes the counts need.
   *
   * @param result - The statistics.
   * @returns The text.
   */
  #summary(result: FieldStatisticsResult): string {
    const parts = [this.localize.term("searchManager_fieldStatsDocuments", formatCount(result.documentCount))];
    if (result.deletedDocumentCount != null)
      parts.push(this.localize.term("searchManager_fieldStatsDeleted", formatCount(result.deletedDocumentCount)));
    if (result.documentsPerCulture) parts.push(this.localize.term("searchManager_fieldStatsPerCulture"));
    if (!result.coverageExact) parts.push(this.localize.term("searchManager_fieldStatsEstimate"));
    // The engine reason once here, rather than on every missing count it leaves unlinked.
    const anyField = result.fields.find((field) => canFilterOn(field, result.target.engine));
    const noMissing = anyField ? missingUnsupportedReason(anyField, this._rules) : undefined;
    if (noMissing) parts.push(this.localize.term("searchManager_fieldStatsNoMissingLink", noMissing));
    return parts.join(" · ");
  }

  /**
   * Renders the table of fields.
   *
   * @param rows - The rows to show.
   * @param result - The statistics, for the target and document count.
   * @returns The template.
   */
  #renderTable(rows: Array<FieldStatistic>, result: FieldStatisticsResult) {
    // The table is wider than a narrow workspace; it scrolls on its own instead of the page.
    return html`
      <div class="scroll">
        <uui-table aria-label=${this.localize.term("searchManager_fieldStatsTableLabel")}>
          <uui-table-head>
            <uui-table-head-cell>${this.localize.term("searchManager_fieldStatsColumnField")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("searchManager_fieldStatsColumnType")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("searchManager_fieldStatsColumnAnalyzer")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("searchManager_fieldStatsColumnCoverage")}</uui-table-head-cell>
            <uui-table-head-cell class="number"
              >${this.localize.term("searchManager_fieldStatsColumnUnique")}</uui-table-head-cell
            >
            <uui-table-head-cell>${this.localize.term("searchManager_fieldStatsColumnTopTerms")}</uui-table-head-cell>
            <uui-table-head-cell class="number"
              >${this.localize.term("searchManager_fieldStatsColumnMissing")}</uui-table-head-cell
            >
          </uui-table-head>
          ${rows.map((field) => this.#renderRow(field, result))}
        </uui-table>
      </div>
    `;
  }

  /**
   * Renders one field's row. `data-field` carries the field name for tests and styling hooks.
   *
   * @param field - The field.
   * @param result - The statistics.
   * @returns The template.
   */
  #renderRow(field: FieldStatistic, result: FieldStatisticsResult) {
    const percent = coveragePercent(field, result.documentCount);
    return html`
      <uui-table-row data-field=${field.name}>
        <uui-table-cell>
          <div class="mono name">${field.name}</div>
          ${this.#describe(field) ? html`<div class="sub">${this.#describe(field)}</div>` : nothing}
          ${field.physical && field.physical !== field.name ? html`<div class="sub mono">${field.physical}</div>` : nothing}
        </uui-table-cell>
        <uui-table-cell><uui-tag look="secondary">${field.type}</uui-tag></uui-table-cell>
        <uui-table-cell class="sub">${this.#renderAnalyzer(field, result)}</uui-table-cell>
        <uui-table-cell>
          ${
            field.storedOnly
              ? html`<span class="sub">${this.localize.term("searchManager_fieldStatsStoredOnly")}</span>`
              : percent === undefined
                ? "-"
                : html`<div class="coverage">
                    <uui-progress-bar progress=${percent}></uui-progress-bar>
                    <span>${coverage(field, result.documentCount)}</span>
                  </div>`
          }
        </uui-table-cell>
        <uui-table-cell class="number">${this.#uniqueTerms(field)}</uui-table-cell>
        <uui-table-cell>${this.#renderTopTerms(field, result)}</uui-table-cell>
        <uui-table-cell class="number">${this.#renderMissing(field, result)}</uui-table-cell>
      </uui-table-row>
    `;
  }

  /**
   * The line under a field's name: its label (segments are labelled by the server), otherwise
   * what kind of row it is when it is not a plain field.
   *
   * @param field - The field.
   * @returns The text, or empty for a plain field without a label.
   */
  #describe(field: FieldStatistic): string {
    if (field.label) return field.label;
    switch (field.group) {
      case "System":
        return this.localize.term("searchManager_fieldStatsGroupSystem");
      case "CultureVariant":
        return this.localize.term("searchManager_fieldStatsGroupCulture", field.culture ?? "");
      case "RawCopy":
        return this.localize.term("searchManager_fieldStatsGroupRaw");
      case "Segment":
        return this.localize.term("searchManager_fieldStatsGroupSegment");
      default:
        return "";
    }
  }

  /**
   * Renders the analyzer name as a link into the Analyzer tool, on this index and field, so the
   * user can see how a value is broken into terms. Stored-only fields have nothing to analyse, and
   * numbers and dates are written as numeric terms without an analyzer.
   *
   * @param field - The field.
   * @param result - The statistics, for the index name.
   * @returns The template, or `-` without an analyzer.
   */
  #renderAnalyzer(field: FieldStatistic, result: FieldStatisticsResult) {
    if (!field.analyzer) return "-";
    if (field.storedOnly || field.type === "Number" || field.type === "Date") return field.analyzer;
    return html`<a
      class="analyzer"
      href=${analyzerUrl(result.target.name, field.name)}
      title=${this.localize.term("searchManager_analyzerTryField", field.name)}
      >${field.analyzer}</a
    >`;
  }

  /**
   * The unique-term count, "at least N" when the server stopped counting.
   *
   * @param field - The field.
   * @returns The text.
   */
  #uniqueTerms(field: FieldStatistic): string {
    if (field.storedOnly || field.uniqueTerms == null) return "-";
    const count = formatCount(field.uniqueTerms);
    return field.uniqueTermsSampled ? this.localize.term("searchManager_fieldStatsAtLeast", count) : count;
  }

  /**
   * Renders the top terms as links into Explore (or plain tags when the field cannot be filtered
   * on), or a date field's range.
   *
   * @param field - The field.
   * @param result - The statistics.
   * @returns The template.
   */
  #renderTopTerms(field: FieldStatistic, result: FieldStatisticsResult) {
    if (field.storedOnly) return "-";
    if (field.type === "Date") {
      return field.range ? this.localize.term("searchManager_fieldStatsRange", field.range.min, field.range.max) : "-";
    }
    if (field.topTerms.length === 0) return "-";

    const linkable = canFilterOn(field, result.target.engine);
    return html`
      <div class="terms">
        ${field.topTerms.map((term) => {
          const filter = linkable ? termFilter(field, term.value) : undefined;
          const content = html`<span class="term">${term.value}</span
            ><span class="count">${formatCount(term.count)}</span>`;
          // A uui-button with href renders a real link: keyboard-reachable, and it opens in a new
          // tab on middle-click, which suits comparing several terms.
          return filter
            ? html`<uui-button
                compact
                look="outline"
                href=${exploreUrl(result.target.name, {
                  filters: [filter],
                  culture: result.exploreCulture ?? undefined,
                })}
                label=${this.localize.term("searchManager_fieldsInclude", field.name, term.value)}
                >${content}</uui-button
              >`
            : html`<uui-tag look="secondary">${content}</uui-tag>`;
        })}
      </div>
      ${field.analyzedTerms ? html`<div class="sub">${this.localize.term("searchManager_fieldStatsAnalyzed")}</div>` : nothing}
    `;
  }

  /**
   * Renders the missing count, as a link to Explore with an "is missing" chip when the engine
   * runs one.
   *
   * @param field - The field.
   * @param result - The statistics.
   * @returns The template.
   */
  #renderMissing(field: FieldStatistic, result: FieldStatisticsResult) {
    if (field.storedOnly || field.missing == null) return "-";
    const count = formatCount(field.missing);
    if (field.missing === 0 || !canFilterOn(field, result.target.engine)) return count;

    const reason = missingUnsupportedReason(field, this._rules);
    if (reason) return html`<span title=${reason}>${count}</span>`;
    return html`<uui-button
      class="missing"
      compact
      look="secondary"
      href=${exploreUrl(result.target.name, {
        filters: [missingFilter(field)],
        culture: result.exploreCulture ?? undefined,
      })}
      label=${this.localize.term("searchManager_fieldStatsMissingLink", count, field.name)}
      >${this.localize.term("searchManager_fieldStatsMissing", count)}</uui-button
    >`;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
      }

      .headline h4 {
        margin: 0;
      }

      .sub,
      .summary,
      .count {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      .mono {
        font-family: monospace;
        overflow-wrap: anywhere;
      }

      .controls {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }

      .loading {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        color: var(--uui-color-text-alt);
      }

      .scroll {
        overflow-x: auto;
      }

      uui-table-cell {
        vertical-align: top;
      }

      .number {
        text-align: right;
        font-variant-numeric: tabular-nums;
      }

      .coverage {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        min-width: 8rem;
      }

      .coverage uui-progress-bar {
        flex: 1;
      }

      .terms {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-1);
        --uui-button-height: var(--uui-size-7);
      }

      .term {
        font-family: monospace;
      }

      .term + .count {
        margin-left: var(--uui-size-space-2);
      }

      .error {
        color: var(--uui-color-danger-standalone);
      }
    `,
  ];
}

declare global {
  interface HTMLElementTagNameMap {
    "search-manager-field-statistics": SearchManagerFieldStatisticsElement;
  }
}
