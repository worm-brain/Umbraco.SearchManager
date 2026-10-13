import { describe, expect, it } from "vitest";
import type { FieldStatistic, UnsupportedFilter } from "../../api/index.js";
import {
  canFilterOn,
  coveragePercent,
  missingFilter,
  missingUnsupportedReason,
  termFilter,
  visibleFields,
} from "./field-statistics-model.js";

/** A row with the given overrides; an indexed text field in the `Field` group by default. */
function field(overrides: Partial<FieldStatistic> = {}): FieldStatistic {
  return {
    name: "bodyText",
    type: "Text",
    group: "Field",
    uniqueTermsSampled: false,
    topTerms: [],
    analyzedTerms: true,
    storedOnly: false,
    ...overrides,
  };
}

describe("visibleFields", () => {
  const rows = [
    field({ name: "bodyText" }),
    field({ name: "__NodeTypeAlias", group: "System", type: "Keyword" }),
    field({ name: "nodeName_en-us", group: "CultureVariant", culture: "en-US" }),
    field({ name: "title", physical: "Field_title_texts" }),
  ];

  it("shows only Field rows while the checkbox is off", () => {
    expect(visibleFields(rows, { variants: false }).map((row) => row.name)).toEqual(["bodyText", "title"]);
  });

  it("shows every group with the checkbox on", () => {
    expect(visibleFields(rows, { variants: true })).toHaveLength(4);
  });

  it("filters on the physical name too, case-insensitively", () => {
    expect(visibleFields(rows, { variants: false, filter: " FIELD_TITLE" }).map((row) => row.name)).toEqual(["title"]);
  });

  it("shows nothing when no name matches", () => {
    expect(visibleFields(rows, { variants: true, filter: "nope" })).toEqual([]);
  });
});

describe("canFilterOn", () => {
  it("links every indexed core Examine row, system fields included", () => {
    expect(canFilterOn(field({ name: "__NodeTypeAlias", group: "System" }), "examine")).toBe(true);
  });

  it("links only Field rows of an Umbraco Search index", () => {
    expect(
      [field(), field({ group: "System" }), field({ group: "Segment" })].map((row) =>
        canFilterOn(row, "umbraco-search"),
      ),
    ).toEqual([true, false, false]);
  });

  it("never links a stored-only field", () => {
    expect(canFilterOn(field({ storedOnly: true }), "examine")).toBe(false);
  });
});

describe("termFilter", () => {
  it("picks the operator by field type", () => {
    expect((["Text", "Keyword", "Number"] as const).map((type) => termFilter(field({ type }), "x")?.op)).toEqual([
      "Contains",
      "Is",
      "Equals",
    ]);
  });

  it("gives no chip for a date field", () => {
    expect(termFilter(field({ type: "Date" }), "2026-10-12")).toBeUndefined();
  });
});

describe("missingUnsupportedReason", () => {
  it("is undefined when the engine runs is-missing chips", () => {
    expect(missingUnsupportedReason(field(), [])).toBeUndefined();
  });

  it("gives the engine's reason when it has no is-missing", () => {
    const rules: Array<UnsupportedFilter> = [
      { kind: "Field", types: [], operators: ["Missing"], reason: "Umbraco Search has no is missing" },
    ];
    expect(missingUnsupportedReason(field(), rules)).toBe("Umbraco Search has no is missing");
  });

  it("builds an is-missing chip on the field name", () => {
    expect(missingFilter(field())).toEqual({
      kind: "Field",
      field: "bodyText",
      op: "Missing",
      value: "",
      exclude: false,
    });
  });
});

describe("coveragePercent", () => {
  it("rounds the share of documents with a value", () => {
    expect(coveragePercent(field({ documents: 2 }), 3)).toBe(67);
  });

  it("is undefined for an empty index or an unknown count", () => {
    expect([coveragePercent(field({ documents: 2 }), 0), coveragePercent(field(), 10)]).toEqual([undefined, undefined]);
  });
});
