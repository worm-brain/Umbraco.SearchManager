import type { ExploreSchema, FieldType, Filter, FilterOperator } from "../api/index.js";
import { operatorsFor, resolveField } from "./filters.js";

/** The chip editor's form: a chip whose value is being typed. */
export interface ChipDraft {
  kind: Filter["kind"];
  /** The field of a field chip; not editable. */
  field: string | null;
  op: FilterOperator;
  value: string;
  exclude: boolean;
}

/**
 * Builds the editor form for a chip.
 *
 * @param filter - The chip.
 * @returns The form.
 */
export function draftFromFilter(filter: Filter): ChipDraft {
  return {
    kind: filter.kind,
    field: filter.field ?? null,
    op: filter.op,
    value: filter.value,
    exclude: filter.exclude,
  };
}

/**
 * A form for a new chip on a field, with the field type's first operator, as "All fields" opens it.
 *
 * @param field - The field name.
 * @param type - Its type.
 * @param schema - The target's schema, for the operators.
 * @returns The form.
 */
export function newFieldDraft(field: string, type: FieldType, schema: ExploreSchema | undefined): ChipDraft {
  return { kind: "Field", field, op: operatorsFor(schema, type)[0] ?? "Is", value: "", exclude: false };
}

/**
 * Writes the form back as a chip. A "missing" chip carries no value.
 *
 * @param draft - A form that passes {@link isDraftValid}.
 * @returns The chip.
 */
export function filterFromDraft(draft: ChipDraft): Filter {
  return {
    kind: draft.kind,
    field: draft.kind === "Field" ? draft.field : null,
    op: draft.op,
    value: draft.op === "Missing" ? "" : draft.value.trim(),
    exclude: draft.exclude,
  };
}

/**
 * The operators the editor offers: those of the field's type for a field chip (the current one
 * alone when the index has no such field), contains or phrase for free text, none for Lucene.
 *
 * @param draft - The form.
 * @param schema - The target's schema.
 * @returns The operators in display order.
 */
export function editorOperators(draft: ChipDraft, schema: ExploreSchema | undefined): ReadonlyArray<FilterOperator> {
  if (draft.kind === "Text") return ["Contains", "Phrase"];
  if (draft.kind === "Lucene") return [];
  const field = resolveField(schema, draft.field ?? "");
  return field ? operatorsFor(schema, field.type) : [draft.op];
}

/**
 * The value input's hint for the field type, so dates and numbers are typed in the form the
 * server reads.
 *
 * @param draft - The form.
 * @param schema - The target's schema.
 * @returns A localisation key.
 */
export function valuePlaceholderKey(draft: ChipDraft, schema: ExploreSchema | undefined): string {
  const type = draft.kind === "Field" ? resolveField(schema, draft.field ?? "")?.type : undefined;
  if (type === "Date") return "searchManager_editorDateHint";
  if (type === "Number") return "searchManager_editorNumberHint";
  return "searchManager_editorValue";
}

/**
 * Whether Apply may be pressed.
 *
 * @param draft - The form.
 * @returns `true` when there is a value, or the operator needs none.
 */
export function isDraftValid(draft: ChipDraft): boolean {
  return draft.op === "Missing" || draft.value.trim().length > 0;
}
