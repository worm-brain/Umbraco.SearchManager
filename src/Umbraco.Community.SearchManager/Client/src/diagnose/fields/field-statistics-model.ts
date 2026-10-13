import type { FieldStatistic, FieldType, Filter, FilterOperator, UnsupportedFilter } from "../../api/index.js";
import { unsupportedReason } from "../../explore/filters.js";

/** The operator of the chip a top term opens Explore with, per field type; dates have none. */
const TERM_OPERATORS: Partial<Record<FieldType, FilterOperator>> = {
  Text: "Contains",
  Keyword: "Is",
  Number: "Equals",
};

/** Which rows of the field statistics table show. */
export interface FieldRowOptions {
  /** Whether rows outside the `Field` group (system, culture variants, raw copies, segments) show. */
  variants: boolean;
  /** Text the field name, label or physical name must contain, case-insensitively. */
  filter?: string;
}

/**
 * The rows the table shows: `Field` rows always, the other groups only with `variants` on, all
 * narrowed by the name filter. The server's order is kept.
 *
 * @param fields - Every field the server returned.
 * @param options - The checkbox and the filter text.
 * @returns The rows to show.
 */
export function visibleFields(fields: ReadonlyArray<FieldStatistic>, options: FieldRowOptions): Array<FieldStatistic> {
  const query = (options.filter ?? "").trim().toLowerCase();
  return fields.filter(
    (field) =>
      (options.variants || field.group === "Field") &&
      (!query || [field.name, field.label, field.physical].some((text) => (text ?? "").toLowerCase().includes(query))),
  );
}

/**
 * Whether Explore can filter on a field, so its top terms and missing count link there. Core
 * Examine rows name real Lucene fields, so every indexed row can. Umbraco Search filters take a
 * property alias only (ADR 0016): its system fields (`Sys_*`, `__*`) and segment rows have no
 * filter, so only `Field` rows link. A stored-only field is never searchable.
 *
 * @param field - The row.
 * @param engine - The target's engine, `examine` or `umbraco-search`.
 * @returns `true` when chips on the field run.
 */
export function canFilterOn(field: FieldStatistic, engine: string): boolean {
  if (field.storedOnly) return false;
  return engine === "examine" || field.group === "Field";
}

/**
 * The chip a top term opens Explore with: `Contains` for text (the term is one analysed word),
 * `Is` for keywords, `Equals` for numbers. Dates have no top terms, so no chip.
 *
 * @param field - The row.
 * @param value - The term.
 * @returns The chip, or `undefined` for a date field.
 */
export function termFilter(field: FieldStatistic, value: string): Filter | undefined {
  const op = TERM_OPERATORS[field.type];
  return op ? { kind: "Field", field: field.name, op, value, exclude: false } : undefined;
}

/**
 * The chip that finds the documents without a value in the field.
 *
 * @param field - The row.
 * @returns An "is missing" chip.
 */
export function missingFilter(field: FieldStatistic): Filter {
  return { kind: "Field", field: field.name, op: "Missing", value: "", exclude: false };
}

/**
 * Why the missing count cannot link to Explore: the engine's own rule for "is missing" chips
 * (Umbraco Search has none), from the target's schema.
 *
 * @param field - The row.
 * @param rules - The target schema's `unsupported` rules; empty while unknown.
 * @returns The engine's reason, or `undefined` when the link works.
 */
export function missingUnsupportedReason(
  field: FieldStatistic,
  rules: ReadonlyArray<UnsupportedFilter>,
): string | undefined {
  return unsupportedReason(rules, missingFilter(field), field.type);
}

/**
 * A field's coverage as a whole percentage, for the coverage bar.
 *
 * @param field - The row.
 * @param documentCount - Live documents in the index, the base of coverage.
 * @returns 0 to 100, or `undefined` when either count is unknown.
 */
export function coveragePercent(field: FieldStatistic, documentCount: number): number | undefined {
  if (field.documents == null || documentCount <= 0) return undefined;
  return Math.max(0, Math.min(100, Math.round((field.documents / documentCount) * 100)));
}
