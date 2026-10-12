import type { FieldType, Filter } from "../api/index.js";

/** The groups the document drawer shows stored fields in, in display order. */
export type FieldGroupKey = "system" | "properties" | "cultures" | "raw";

/** One group of stored fields. */
export interface FieldGroup {
  key: FieldGroupKey;
  fields: Array<{ name: string; values: Array<string> }>;
}

/**
 * Fields Umbraco writes for every content, media and member item, shown as "System" with the
 * `__` fields Examine and Umbraco add.
 */
const UMBRACO_FIELDS = new Set(
  [
    "id",
    "key",
    "parentID",
    "level",
    "creatorID",
    "creatorName",
    "writerID",
    "writerName",
    "sortOrder",
    "createDate",
    "updateDate",
    "nodeName",
    "urlName",
    "path",
    "nodeType",
    "templateID",
    "icon",
  ].map((name) => name.toLowerCase()),
);

/**
 * Groups a document's stored fields for the drawer: system fields, properties, culture variants
 * (a field ending in one of the site's cultures, such as `nodeName_en-us`) and the unanalysed
 * `__Raw_` copies. Each group is sorted by name; empty groups are left out.
 *
 * @param fields - The stored fields and their values.
 * @param cultures - The site's cultures, any case.
 * @returns The non-empty groups in display order.
 */
export function groupFields(fields: Record<string, Array<string>>, cultures: ReadonlyArray<string>): Array<FieldGroup> {
  const suffixes = cultures.map((culture) => `_${culture.toLowerCase()}`);
  const groups: Record<FieldGroupKey, FieldGroup["fields"]> = { system: [], properties: [], cultures: [], raw: [] };

  for (const name of Object.keys(fields).sort((a, b) => a.localeCompare(b, "en", { sensitivity: "base" }))) {
    const entry = { name, values: fields[name] ?? [] };
    const lower = name.toLowerCase();
    if (lower.startsWith("__raw_")) groups.raw.push(entry);
    else if (suffixes.some((suffix) => lower.endsWith(suffix))) groups.cultures.push(entry);
    else if (lower.startsWith("__") || UMBRACO_FIELDS.has(lower)) groups.system.push(entry);
    else groups.properties.push(entry);
  }

  return (["system", "properties", "cultures", "raw"] as const)
    .map((key) => ({ key, fields: groups[key] }))
    .filter((group) => group.fields.length > 0);
}

/** Longest text value the drawer offers to filter on as a phrase. */
const MAX_PHRASE = 80;

/**
 * The chip "filter for" / "filter out" adds for a stored value: equality on keywords and
 * numbers, the day on dates (stored as ticks), the exact phrase on short text.
 *
 * @param field - The field name.
 * @param type - Its type in the target's schema.
 * @param value - The stored value.
 * @param exclude - Whether to filter the value out.
 * @returns The chip, or `undefined` when the value cannot be filtered on (long text, a date
 *   that is not ticks, an empty value).
 */
export function filterForValue(field: string, type: FieldType, value: string, exclude: boolean): Filter | undefined {
  const text = value.trim();
  if (!text) return undefined;
  switch (type) {
    case "Keyword":
      return { kind: "Field", field, op: "Is", value: text, exclude };
    case "Number":
      return { kind: "Field", field, op: "Equals", value: text, exclude };
    case "Date": {
      const date = ticksToDate(text);
      return date ? { kind: "Field", field, op: "On", value: date.slice(0, 10), exclude } : undefined;
    }
    default:
      return text.length <= MAX_PHRASE ? { kind: "Field", field, op: "Phrase", value: text, exclude } : undefined;
  }
}

/** .NET ticks at the Unix epoch. */
const EPOCH_TICKS = 621_355_968_000_000_000n;

/**
 * Reads a date Examine stored as .NET ticks, for showing next to the raw value.
 *
 * @param value - The stored value.
 * @returns For example `2026-03-01 23:59:00`, or `undefined` when the value is not ticks.
 */
export function ticksToDate(value: string): string | undefined {
  if (!/^\d{15,19}$/.test(value)) return undefined;
  const milliseconds = Number((BigInt(value) - EPOCH_TICKS) / 10_000n);
  const date = new Date(milliseconds);
  if (Number.isNaN(date.getTime())) return undefined;
  // Ticks carry no time zone; reading them as UTC shows the stored wall-clock time unchanged.
  return date.toISOString().slice(0, 19).replace("T", " ");
}

/**
 * A document as JSON for "Copy as JSON": its id and every stored field, single values unwrapped.
 *
 * @param id - The document id.
 * @param fields - The stored fields.
 * @returns Indented JSON.
 */
export function documentJson(id: string, fields: Record<string, Array<string>>): string {
  const flat: Record<string, string | Array<string>> = {};
  for (const [name, values] of Object.entries(fields)) flat[name] = values.length === 1 ? values[0]! : values;
  return JSON.stringify({ id, fields: flat }, null, 2);
}
