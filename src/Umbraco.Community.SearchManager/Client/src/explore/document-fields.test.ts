import { describe, expect, it } from "vitest";
import { documentJson, filterForValue, groupFields, ticksToDate } from "./document-fields.js";
import { searchKeyAction } from "./search-keys.js";

describe("groupFields", () => {
  it("groups system, properties, culture variants and raw copies", () => {
    const groups = groupFields(
      {
        bodyText: ["Hello"],
        __NodeId: ["1"],
        nodeName: ["Home"],
        "nodeName_da-dk": ["Hjem"],
        "__Published_en-us": ["y"],
        __Raw_bodyText: ["Hello"],
      },
      ["da-DK", "en-US"],
    );
    expect(groups.map((group) => [group.key, group.fields.map((field) => field.name)])).toEqual([
      ["system", ["__NodeId", "nodeName"]],
      ["properties", ["bodyText"]],
      ["cultures", ["__Published_en-us", "nodeName_da-dk"]],
      ["raw", ["__Raw_bodyText"]],
    ]);
  });

  it("leaves out empty groups", () => {
    expect(groupFields({ bodyText: ["x"] }, []).map((group) => group.key)).toEqual(["properties"]);
  });
});

describe("ticksToDate", () => {
  it("reads .NET ticks as the stored wall-clock time", () => {
    expect(ticksToDate("639080063400000000")).toBe("2026-03-01 23:59:00");
  });

  it("ignores values that are not ticks", () => {
    expect(ticksToDate("3")).toBeUndefined();
  });
});

describe("filterForValue", () => {
  it("filters a keyword by equality", () => {
    expect(filterForValue("__NodeTypeAlias", "Keyword", "product", false)).toEqual({
      kind: "Field",
      field: "__NodeTypeAlias",
      op: "Is",
      value: "product",
      exclude: false,
    });
  });

  it("filters a date stored as ticks by its day", () => {
    expect(filterForValue("updateDate", "Date", "639080063400000000", true)).toMatchObject({
      op: "On",
      value: "2026-03-01",
      exclude: true,
    });
  });

  it("filters short text as a phrase", () => {
    expect(filterForValue("nodeName", "Text", "Red hoodie", false)).toMatchObject({
      op: "Phrase",
      value: "Red hoodie",
    });
  });

  it("offers no filter for long text", () => {
    expect(filterForValue("bodyText", "Text", "x".repeat(81), false)).toBeUndefined();
  });
});

describe("documentJson", () => {
  it("unwraps single values", () => {
    expect(JSON.parse(documentJson("5", { a: ["1"], b: ["2", "3"] }))).toEqual({
      id: "5",
      fields: { a: "1", b: ["2", "3"] },
    });
  });
});

describe("searchKeyAction", () => {
  it("submits on Enter with text", () => {
    expect(searchKeyAction({ key: "Enter", isComposing: false }, "type:x", 0)).toBe("submit");
  });

  it("removes the last chip on Backspace in an empty input", () => {
    expect(searchKeyAction({ key: "Backspace", isComposing: false }, "", 2)).toBe("removeLastChip");
  });

  it("lets Backspace edit text", () => {
    expect(searchKeyAction({ key: "Backspace", isComposing: false }, "ty", 2)).toBeUndefined();
  });

  it("ignores Enter that confirms an IME composition", () => {
    expect(searchKeyAction({ key: "Enter", isComposing: true }, "x", 0)).toBeUndefined();
  });
});
