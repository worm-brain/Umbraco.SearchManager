import { describe, expect, it } from "vitest";
import type { ExploreSchema } from "../api/index.js";
import {
  coverage,
  dateLabel,
  fieldSuggestions,
  moveHighlight,
  readInput,
  valueOperator,
  valueSuggestions,
} from "./suggestions.js";
import { schema as baseSchema, term } from "./term.test-helper.js";

const schema: ExploreSchema = {
  ...baseSchema,
  documentCount: 200,
  fields: [
    { name: "__NodeTypeAlias", type: "Keyword", documents: 200 },
    { name: "level", type: "Number", documents: 200 },
    { name: "nodeName", type: "Text", documents: 200 },
    { name: "bodyText", type: "Text", documents: 150 },
    { name: "updateDate", type: "Date", documents: 200 },
    { name: "typeface", type: "Text", documents: 1 },
  ],
};

describe("readInput", () => {
  it("reads an exclude on a field with a prefix", () => {
    expect(readInput("-type:pro")).toEqual({ exclude: true, field: "type", prefix: "pro" });
  });

  it("reads plain text as a prefix with no field", () => {
    expect(readInput("umbr")).toEqual({ exclude: false, prefix: "umbr" });
  });

  it("does not read a URL scheme as a field", () => {
    expect(readInput("a b:c").field).toBeUndefined();
  });
});

describe("coverage", () => {
  it("is the share of documents with the field", () => {
    expect(coverage({ name: "bodyText", type: "Text", documents: 150 }, 200)).toBe("75%");
  });

  it("shows under one percent rather than zero", () => {
    expect(coverage({ name: "x", type: "Text", documents: 1 }, 200)).toBe("<1%");
  });

  it("is empty when unknown", () => {
    expect(coverage({ name: "x", type: "Text" }, 200)).toBe("");
  });
});

describe("fieldSuggestions", () => {
  it("puts aliased fields first when nothing is typed", () => {
    const rows = fieldSuggestions(schema, readInput(""));
    expect(rows.slice(0, 2).map((row) => (row.kind === "field" ? row.insert : ""))).toEqual(["type:", "updated:"]);
  });

  it("matches an alias prefix before a name that only contains the text", () => {
    const rows = fieldSuggestions(schema, readInput("ty"));
    expect(rows.map((row) => row.label)).toEqual(["Document type", "typeface", "ty"]);
  });

  it("carries the exclusion into what picking inserts", () => {
    const [first] = fieldSuggestions(schema, readInput("-ty"));
    expect(first?.kind === "field" ? first.insert : "").toBe("-type:");
  });

  it("ends with a row that searches the typed text", () => {
    const rows = fieldSuggestions(schema, readInput("hoodie"));
    expect(rows.at(-1)).toMatchObject({ kind: "text", filter: { kind: "Text", value: "hoodie" } });
  });
});

describe("valueSuggestions", () => {
  it("turns keyword terms into equality chips with counts", () => {
    const rows = valueSuggestions(schema, readInput("type:pr"), [{ value: "product", count: 2104 }]);
    expect(rows).toEqual([
      {
        kind: "value",
        label: "product",
        count: "2,104",
        term: false,
        filter: { kind: "Field", field: "__NodeTypeAlias", op: "Is", value: "product", exclude: false },
      },
    ]);
  });

  it("labels text field values as terms", () => {
    const [row] = valueSuggestions(schema, readInput("nodeName:ho"), [{ value: "hoodie", count: 3 }]);
    expect(row).toMatchObject({ term: true, filter: { op: "Contains" } });
  });

  it("offers relative ranges for a date field", () => {
    const rows = valueSuggestions(schema, readInput("updated:-"), []);
    expect(rows.map((row) => row.label)).toEqual(["-7d", "-30d", "-365d"]);
  });

  it("offers nothing for an unknown field", () => {
    expect(valueSuggestions(schema, readInput("colour:r"), [{ value: "red", count: 1 }])).toEqual([]);
  });
});

describe("valueOperator", () => {
  it("uses on or after for a relative date and on for a day", () => {
    expect([valueOperator("Date", "-7d"), valueOperator("Date", "2026-01-01")]).toEqual(["After", "On"]);
  });
});

describe("moveHighlight", () => {
  it("starts at the top going down", () => {
    expect(moveHighlight(-1, 1, 3)).toBe(0);
  });

  it("wraps from the top to the bottom going up", () => {
    expect(moveHighlight(0, -1, 3)).toBe(2);
  });

  it("is none when there are no rows", () => {
    expect(moveHighlight(0, 1, 0)).toBe(-1);
  });
});

describe("dateLabel", () => {
  it("reads a relative range as last N days", () => {
    expect(dateLabel("-30d", term)).toBe("last 30 days");
  });

  it("keeps a day as it is", () => {
    expect(dateLabel("2026-01-01", term)).toBe("2026-01-01");
  });
});
