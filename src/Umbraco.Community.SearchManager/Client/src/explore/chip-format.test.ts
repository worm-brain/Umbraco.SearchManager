import { describe, expect, it } from "vitest";
import type { Filter } from "../api/index.js";
import { describeFilter } from "./chip-format.js";
import { schema, term } from "./term.test-helper.js";

const field = (op: Filter["op"], value: string, exclude = false, name = "level"): Filter => ({
  kind: "Field",
  field: name,
  op,
  value,
  exclude,
});

describe("describeFilter", () => {
  it("reads a field chip as a sentence with the alias label", () => {
    expect(describeFilter(field("Is", "product", false, "__NodeTypeAlias"), schema, term)).toEqual({
      kind: "include",
      label: "Document type is product",
      description: '__NodeTypeAlias is "product"',
    });
  });

  it("uses the operator word for comparisons", () => {
    expect(describeFilter(field("AtLeast", "3"), schema, term).label).toBe("level is at least 3");
  });

  it("prefixes an excluded chip with not", () => {
    expect(describeFilter(field("Equals", "3", true), schema, term)).toMatchObject({
      kind: "exclude",
      label: "not level is 3",
    });
  });

  it("reads an excluded missing chip as has a value", () => {
    expect(describeFilter(field("Missing", "", true), schema, term)).toMatchObject({
      kind: "include",
      label: "level has a value",
    });
  });

  it("quotes free text", () => {
    const text: Filter = { kind: "Text", field: null, op: "Contains", value: "umbraco guide", exclude: false };
    expect(describeFilter(text, schema, term)).toMatchObject({ kind: "text", label: '"umbraco guide"' });
  });

  it("labels raw Lucene", () => {
    const lucene: Filter = { kind: "Lucene", field: null, op: "Contains", value: "a OR b", exclude: false };
    expect(describeFilter(lucene, schema, term).label).toBe("Lucene: a OR b");
  });
});
