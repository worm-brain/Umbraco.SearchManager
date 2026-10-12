import type { ExploreSchema, Filter } from "../api/index.js";
import { fieldLabel, type Term } from "./filters.js";

/** How a chip is styled: `include` keeps matches, `exclude` removes them, `text` is free text. */
export type ChipKind = "include" | "exclude" | "text";

/** What a chip shows. */
export interface ChipDisplay {
  kind: ChipKind;
  /** The pill text, for example `Document type is product`. */
  label: string;
  /** The tooltip and accessible name, with the field's real name. */
  description: string;
}

/**
 * Describes a chip for display. Field chips read as a sentence with the alias label and the
 * operator word (`Level is at least 3`); excluded chips are prefixed with "not"; an excluded
 * "is missing" reads "has a value". Values are plain text, never markup.
 *
 * @param filter - The chip.
 * @param schema - The target's schema, for alias labels; may be unknown while it loads.
 * @param term - Localises labels and operator words.
 * @returns The kind, label and description.
 */
export function describeFilter(filter: Filter, schema: ExploreSchema | undefined, term: Term): ChipDisplay {
  if (filter.kind === "Text") {
    const label = `"${filter.value}"`;
    const op = term(filter.op === "Phrase" ? "searchManager_opPhrase" : "searchManager_opContains");
    const description = term("searchManager_chipDescription", term("searchManager_fieldText"), op, label);
    return filter.exclude
      ? {
          kind: "exclude",
          label: term("searchManager_chipNot", label),
          description: term("searchManager_chipNot", description),
        }
      : { kind: "text", label, description };
  }

  if (filter.kind === "Lucene") {
    const label = `${term("searchManager_fieldLucene")}: ${filter.value}`;
    return filter.exclude
      ? {
          kind: "exclude",
          label: term("searchManager_chipNot", label),
          description: term("searchManager_chipNot", label),
        }
      : { kind: "include", label, description: label };
  }

  const field = filter.field ?? "";
  const name = fieldLabel(schema, field);
  if (filter.op === "Missing") {
    const key = filter.exclude ? "searchManager_chipHasValue" : "searchManager_chipMissing";
    return {
      kind: filter.exclude ? "include" : "exclude",
      label: term(key, name),
      description: term(key, field),
    };
  }

  const op = term(`searchManager_op${filter.op}`);
  const label = `${name} ${op} ${filter.value}`;
  const description = term("searchManager_chipDescription", field, op, `"${filter.value}"`);
  return filter.exclude
    ? {
        kind: "exclude",
        label: term("searchManager_chipNot", label),
        description: term("searchManager_chipNot", description),
      }
    : { kind: "include", label, description };
}
