import type { ExploreSchema, FieldType, Filter, IndexField, TermCount } from "../api/index.js";
import { fieldLabel, resolveField } from "./filters.js";

/** What the search box input means for suggestions. */
export interface InputContext {
  /** The input started with `-`: suggestions build exclude chips. */
  exclude: boolean;
  /** The field before the colon, when there is one. */
  field?: string;
  /** The text after the colon, or the whole text (without `-`) when there is no colon. */
  prefix: string;
}

/** One row in the suggestion list. */
export type Suggestion =
  | {
      kind: "field";
      /** What picking puts in the input, for example `type:`. */
      insert: string;
      field: IndexField;
      /** The alias label or field name. */
      label: string;
      /** Coverage, for example `72%`, or empty when unknown. */
      coverage: string;
    }
  | {
      kind: "value";
      /** The chip picking adds. */
      filter: Filter;
      /** The value as shown. */
      label: string;
      /** The document count, or empty. */
      count: string;
      /** Whether the value is an analysed term of a text field rather than a whole value. */
      term: boolean;
    }
  | { kind: "text"; filter: Filter; label: string };

/** Most rows the list shows. */
export const MAX_SUGGESTIONS = 8;

/** Relative dates offered for date fields, as chip values. */
export const DATE_SUGGESTIONS = ["today", "-7d", "-30d", "-365d"] as const;

/**
 * Splits the input into its exclusion, field and prefix: `-type:pro` is an exclude on `type`
 * with prefix `pro`; `umbr` is a prefix with no field.
 *
 * @param raw - The input text.
 * @returns The parts.
 */
export function readInput(raw: string): InputContext {
  const exclude = raw.startsWith("-");
  const body = exclude ? raw.slice(1) : raw;
  const colon = body.indexOf(":");
  if (colon > 0 && /^[A-Za-z_][\w.-]*$/.test(body.slice(0, colon))) {
    return { exclude, field: body.slice(0, colon), prefix: body.slice(colon + 1) };
  }
  return { exclude, prefix: body };
}

/**
 * A field's coverage: the share of documents with a value.
 *
 * @param field - The field.
 * @param documentCount - Documents in the index.
 * @returns For example `72%`, `<1%`, or empty when unknown.
 */
export function coverage(field: IndexField, documentCount: number | null | undefined): string {
  if (field.documents == null || !documentCount) return "";
  const percent = (field.documents / documentCount) * 100;
  if (percent > 0 && percent < 1) return "<1%";
  return `${Math.min(100, Math.round(percent))}%`;
}

/**
 * Field suggestions for what was typed before any colon: aliased fields first when nothing is
 * typed, otherwise fields whose alias, label or name starts with the text, then names that
 * contain it; ties go to the field more documents have. A last row searches the text itself.
 *
 * @param schema - The target's schema.
 * @param input - The input context (no field).
 * @returns The rows.
 */
export function fieldSuggestions(schema: ExploreSchema, input: InputContext): Array<Suggestion> {
  const query = input.prefix.toLowerCase();
  const neg = input.exclude ? "-" : "";
  const aliasOf = (field: IndexField) =>
    schema.aliases.find((alias) => alias.field.toLowerCase() === field.name.toLowerCase());

  const ranked = schema.fields
    .map((field) => {
      const alias = aliasOf(field);
      const label = fieldLabel(schema, field.name).toLowerCase();
      const name = field.name.toLowerCase();
      let rank: number;
      if (!query) rank = alias ? 0 : 2;
      else if (alias?.alias.toLowerCase().startsWith(query) || label.startsWith(query)) rank = 0;
      else if (name.startsWith(query)) rank = 1;
      else if (name.includes(query) || label.includes(query)) rank = 2;
      else rank = -1;
      return { field, alias, rank };
    })
    .filter((entry) => entry.rank >= 0)
    .sort((a, b) => a.rank - b.rank || (b.field.documents ?? 0) - (a.field.documents ?? 0));

  const rows: Array<Suggestion> = ranked
    .slice(0, query ? MAX_SUGGESTIONS - 1 : MAX_SUGGESTIONS)
    .map(({ field, alias }) => ({
      kind: "field",
      insert: `${neg}${alias?.alias ?? field.name}:`,
      field,
      label: alias?.label ?? field.name,
      coverage: coverage(field, schema.documentCount),
    }));
  if (query) {
    rows.push({
      kind: "text",
      label: `${neg}${input.prefix}`,
      filter: { kind: "Text", field: null, op: "Contains", value: input.prefix, exclude: input.exclude },
    });
  }
  return rows;
}

/**
 * The operator a picked value gets: equality for keywords and numbers, contains for text
 * terms, "on or after" for a relative date and "on" for a day.
 *
 * @param type - The field type.
 * @param value - The value.
 * @returns The operator.
 */
export function valueOperator(type: FieldType, value: string): Filter["op"] {
  switch (type) {
    case "Keyword":
      return "Is";
    case "Number":
      return "Equals";
    case "Date":
      return /^\d{4}-\d{2}-\d{2}$/.test(value) || value === "today" ? "On" : "After";
    default:
      return "Contains";
  }
}

/**
 * Value suggestions for a field: its most common terms with counts, or relative dates for a
 * date field.
 *
 * @param schema - The target's schema.
 * @param input - The input context (with a field).
 * @param terms - The terms the server returned for the prefix; ignored for dates.
 * @returns The rows; empty when the field is unknown.
 */
export function valueSuggestions(
  schema: ExploreSchema,
  input: InputContext,
  terms: ReadonlyArray<TermCount>,
): Array<Suggestion> {
  const field = resolveField(schema, input.field ?? "");
  if (!field) return [];
  const values =
    field.type === "Date"
      ? DATE_SUGGESTIONS.filter((value) => value.startsWith(input.prefix.toLowerCase())).map((value) => ({
          value,
          count: "",
        }))
      : terms.map((term) => ({ value: term.value, count: term.count.toLocaleString("en-GB") }));
  return values.slice(0, MAX_SUGGESTIONS).map(({ value, count }) => ({
    kind: "value",
    label: value,
    count,
    term: field.type === "Text",
    filter: { kind: "Field", field: field.name, op: valueOperator(field.type, value), value, exclude: input.exclude },
  }));
}

/**
 * Moves the highlighted row, wrapping at both ends; -1 is "none".
 *
 * @param current - The highlighted index, or -1.
 * @param delta - +1 for down, -1 for up.
 * @param length - How many rows there are.
 * @returns The new index, or -1 when there are no rows.
 */
export function moveHighlight(current: number, delta: number, length: number): number {
  if (length === 0) return -1;
  if (current < 0) return delta > 0 ? 0 : length - 1;
  return (current + delta + length) % length;
}

/**
 * How a date chip value reads in lists: `-7d` is "last 7 days", `today` "today", a day as is.
 *
 * @param value - The chip value.
 * @param term - Localises the words.
 * @returns The label.
 */
export function dateLabel(value: string, term: (key: string, ...args: Array<string>) => string): string {
  if (value === "today") return term("searchManager_relativeToday");
  const days = /^-(\d+)d$/.exec(value)?.[1];
  return days ? term("searchManager_relativeDays", days) : value;
}
