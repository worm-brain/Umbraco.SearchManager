import { describe, expect, it } from "vitest";
import type { Filter } from "../api/index.js";
import { draftFromFilter, editorOperators, filterFromDraft, isDraftValid, newFieldDraft } from "./chip-edit.js";
import { schema } from "./term.test-helper.js";

const level: Filter = { kind: "Field", field: "level", op: "Equals", value: "3", exclude: false };

describe("chip editor drafts", () => {
  it("round-trips a chip unchanged", () => {
    expect(filterFromDraft(draftFromFilter(level))).toEqual(level);
  });

  it("drops the value of a missing chip", () => {
    expect(filterFromDraft({ ...draftFromFilter(level), op: "Missing" }).value).toBe("");
  });

  it("trims the value", () => {
    expect(filterFromDraft({ ...draftFromFilter(level), value: " 4 " }).value).toBe("4");
  });

  it("offers the operators of the field type", () => {
    expect(editorOperators(draftFromFilter(level), schema)).toEqual([
      "Equals",
      "GreaterThan",
      "AtLeast",
      "LessThan",
      "AtMost",
      "Missing",
    ]);
  });

  it("offers only the current operator for an unknown field", () => {
    expect(editorOperators({ ...draftFromFilter(level), field: "colour", op: "Is" }, schema)).toEqual(["Is"]);
  });

  it("starts a new field chip on the type's first operator", () => {
    expect(newFieldDraft("updateDate", "Date", schema).op).toBe("On");
  });

  it("needs a value unless the operator is missing", () => {
    expect([
      isDraftValid({ ...draftFromFilter(level), value: " " }),
      isDraftValid({ ...draftFromFilter(level), value: "", op: "Missing" }),
    ]).toEqual([false, true]);
  });
});
