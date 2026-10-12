import type {
  ExploreSchema,
  ExploreTarget,
  FieldType,
  Filter,
  FilterKind,
  FilterOperator,
  IndexField,
} from "../api/index.js";

/** Every `FilterKind`, for validating untrusted input such as a shared URL. */
export const FILTER_KINDS = ["Text", "Field", "Lucene"] as const satisfies ReadonlyArray<FilterKind>;

/** Every `FilterOperator`, for validating untrusted input such as a shared URL. */
export const FILTER_OPERATORS = [
  "Contains",
  "Phrase",
  "StartsWith",
  "Fuzzy",
  "Is",
  "Equals",
  "GreaterThan",
  "AtLeast",
  "LessThan",
  "AtMost",
  "On",
  "After",
  "Before",
  "Missing",
] as const satisfies ReadonlyArray<FilterOperator>;

/** Compile-time proof that {@link FILTER_OPERATORS} lists every generated operator. */
type AllOperatorsListed = Exclude<FilterOperator, (typeof FILTER_OPERATORS)[number]> extends never ? true : never;
const allOperatorsListed: AllOperatorsListed = true;
void allOperatorsListed;

/**
 * Checks that an untrusted value is a well-formed filter (chip).
 *
 * @param value - Anything, for example decoded from the URL.
 * @returns `true` when it has a known kind and operator and the fields their types require.
 */
export function isFilter(value: unknown): value is Filter {
  if (typeof value !== "object" || value === null) return false;
  const filter = value as Record<string, unknown>;
  return (
    (FILTER_KINDS as ReadonlyArray<unknown>).includes(filter.kind) &&
    (FILTER_OPERATORS as ReadonlyArray<unknown>).includes(filter.op) &&
    typeof filter.value === "string" &&
    typeof filter.exclude === "boolean" &&
    (filter.field === null || filter.field === undefined || typeof filter.field === "string")
  );
}

/**
 * Whether two chips filter the same way. Text values compare case-insensitively, as the index
 * analyses them.
 *
 * @param a - One chip.
 * @param b - The other.
 * @returns `true` when kind, field, operator, value and exclusion match.
 */
export function sameFilter(a: Filter, b: Filter): boolean {
  return (
    a.kind === b.kind &&
    (a.field ?? "").toLowerCase() === (b.field ?? "").toLowerCase() &&
    a.op === b.op &&
    a.value.toLowerCase() === b.value.toLowerCase() &&
    a.exclude === b.exclude
  );
}

/**
 * Appends chips, skipping any already present, so pressing Enter twice does not double a filter.
 *
 * @param filters - The current chips.
 * @param added - The chips to add, in order.
 * @returns A new list.
 */
export function appendFilters(filters: ReadonlyArray<Filter>, added: ReadonlyArray<Filter>): Array<Filter> {
  const result = [...filters];
  for (const filter of added) {
    if (!result.some((existing) => sameFilter(existing, filter))) result.push(filter);
  }
  return result;
}

/**
 * Replaces the chip at `index`; when the replacement equals another chip, the edited one is
 * dropped instead, so the list never holds duplicates.
 *
 * @param filters - The current chips.
 * @param index - Position to replace; out of range leaves the list unchanged.
 * @param filter - The replacement.
 * @returns A new list.
 */
export function replaceFilterAt(filters: ReadonlyArray<Filter>, index: number, filter: Filter): Array<Filter> {
  if (index < 0 || index >= filters.length) return [...filters];
  const duplicate = filters.some((existing, i) => i !== index && sameFilter(existing, filter));
  return duplicate
    ? filters.filter((_, i) => i !== index)
    : filters.map((existing, i) => (i === index ? filter : existing));
}

/**
 * Removes the chip at `index`.
 *
 * @param filters - The current chips.
 * @param index - Position to remove.
 * @returns A new list.
 */
export function removeFilterAt(filters: ReadonlyArray<Filter>, index: number): Array<Filter> {
  return filters.filter((_, i) => i !== index);
}

/**
 * Adds the chip, or removes it when it is already there: clicking a value in the fields panel
 * twice turns its filter off again.
 *
 * @param filters - The current chips.
 * @param filter - The chip to toggle.
 * @returns A new list.
 */
export function toggleFilter(filters: ReadonlyArray<Filter>, filter: Filter): Array<Filter> {
  return filters.some((existing) => sameFilter(existing, filter))
    ? filters.filter((existing) => !sameFilter(existing, filter))
    : [...filters, filter];
}

/**
 * Finds a field by alias or name, case-insensitively, as the server's parser does.
 *
 * @param schema - The target's schema.
 * @param nameOrAlias - What the chip holds or the user typed.
 * @returns The field, or `undefined` when the index has none.
 */
export function resolveField(schema: ExploreSchema | undefined, nameOrAlias: string): IndexField | undefined {
  if (!schema) return undefined;
  const wanted = nameOrAlias.toLowerCase();
  const alias = schema.aliases.find((candidate) => candidate.alias.toLowerCase() === wanted);
  const name = (alias?.field ?? nameOrAlias).toLowerCase();
  return schema.fields.find((field) => field.name.toLowerCase() === name);
}

/**
 * The name a chip shows for a field: the alias label ("Document type") when one names it,
 * otherwise the field name itself.
 *
 * @param schema - The target's schema.
 * @param field - The field name.
 * @returns The label.
 */
export function fieldLabel(schema: ExploreSchema | undefined, field: string): string {
  const resolved = resolveField(schema, field)?.name ?? field;
  return schema?.aliases.find((alias) => alias.field.toLowerCase() === resolved.toLowerCase())?.label ?? field;
}

/**
 * The operators the source runs on a field type, in the order the chip editor lists them.
 *
 * @param schema - The target's schema.
 * @param type - The field type.
 * @returns The operators; empty while the schema is unknown.
 */
export function operatorsFor(schema: ExploreSchema | undefined, type: FieldType): ReadonlyArray<FilterOperator> {
  return schema?.operators.find((entry) => entry.type === type)?.operators ?? [];
}

/** Localises a key with arguments, as `this.localize.term` does. */
export type Term = (key: string, ...args: Array<string>) => string;

/**
 * Why a chip cannot run on the current target, so it is drawn disabled and not sent (the server
 * also skips it). Value checks (is it a number, a date) are left to the server's `skipped` list.
 *
 * @param filter - The chip.
 * @param schema - The target's schema; `undefined` while it loads, when nothing is disabled.
 * @param target - The target, for its capabilities.
 * @param term - Localises the reason.
 * @returns The reason, or `undefined` when the chip runs.
 */
export function disabledReason(
  filter: Filter,
  schema: ExploreSchema | undefined,
  target: ExploreTarget | undefined,
  term: Term,
): string | undefined {
  if (!schema || !target) return undefined;
  if (filter.kind === "Lucene") {
    return target.capabilities.nativeQuery ? undefined : term("searchManager_chipNoLucene", target.name);
  }
  if (filter.kind === "Text") return undefined;

  const field = resolveField(schema, filter.field ?? "");
  if (!field) return term("searchManager_chipUnknownField", filter.field ?? "", target.name);
  if (!operatorsFor(schema, field.type).includes(filter.op)) {
    return term("searchManager_chipWrongOperator", term(`searchManager_op${filter.op}`), field.type.toLowerCase());
  }
  return undefined;
}
