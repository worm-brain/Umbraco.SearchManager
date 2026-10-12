import en from "../lang/en.js";
import type { ExploreSchema, ExploreTarget } from "../api/index.js";

/**
 * `localize.term` backed by the English dictionary, so tests read the real copy.
 *
 * @param key - A `searchManager_` key.
 * @param args - Arguments for function entries.
 * @returns The text.
 */
export function term(key: string, ...args: Array<string>): string {
  const entry = (en.searchManager as Record<string, unknown>)[key.replace(/^searchManager_/, "")];
  if (typeof entry === "function") return (entry as (...values: Array<unknown>) => string)(...args);
  return typeof entry === "string" ? entry : key;
}

/** A schema shaped like ExternalIndex's. */
export const schema: ExploreSchema = {
  fields: [
    { name: "__NodeTypeAlias", type: "Keyword" },
    { name: "level", type: "Number" },
    { name: "nodeName", type: "Text" },
    { name: "updateDate", type: "Date" },
  ],
  aliases: [
    { alias: "type", field: "__NodeTypeAlias", label: "Document type" },
    { alias: "updated", field: "updateDate", label: "Updated" },
  ],
  operators: [
    { type: "Text", operators: ["Contains", "Phrase", "StartsWith", "Fuzzy", "Missing"] },
    { type: "Keyword", operators: ["Is", "StartsWith", "Missing"] },
    { type: "Number", operators: ["Equals", "GreaterThan", "AtLeast", "LessThan", "AtMost", "Missing"] },
    { type: "Date", operators: ["On", "After", "Before", "Missing"] },
  ],
  cultures: ["da-DK", "en-US"],
  documentCount: 200,
};

/** A Lucene index target. */
export const target: ExploreTarget = {
  name: "ExternalIndex",
  engine: "examine",
  kind: "Index",
  description: null,
  isMemberData: false,
  capabilities: { nativeQuery: true, termStatistics: true },
};
