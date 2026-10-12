import { describe, expect, it } from "vitest";
import type { Filter } from "../api/index.js";
import {
  appendFilters,
  disabledReason,
  fieldLabel,
  isFilter,
  removeFilterAt,
  replaceFilterAt,
  resolveField,
  toggleFilter,
} from "./filters.js";
import { schema, target, term } from "./term.test-helper.js";

const product: Filter = { kind: "Field", field: "__NodeTypeAlias", op: "Is", value: "product", exclude: false };
const level: Filter = { kind: "Field", field: "level", op: "Equals", value: "3", exclude: false };

describe("appendFilters", () => {
  it("adds new chips in order", () => {
    expect(appendFilters([product], [level])).toEqual([product, level]);
  });

  it("skips a chip already present, ignoring value case", () => {
    expect(appendFilters([product], [{ ...product, value: "PRODUCT" }])).toEqual([product]);
  });
});

describe("replaceFilterAt", () => {
  it("replaces in place", () => {
    expect(replaceFilterAt([product, level], 1, { ...level, value: "4" })).toEqual([product, { ...level, value: "4" }]);
  });

  it("drops the edited chip when it becomes a duplicate", () => {
    expect(replaceFilterAt([product, level], 1, product)).toEqual([product]);
  });
});

describe("removeFilterAt", () => {
  it("removes the chip at the position", () => {
    expect(removeFilterAt([product, level], 0)).toEqual([level]);
  });
});

describe("toggleFilter", () => {
  it("adds a chip that is not there", () => {
    expect(toggleFilter([level], product)).toEqual([level, product]);
  });

  it("removes a chip that is there", () => {
    expect(toggleFilter([product, level], product)).toEqual([level]);
  });
});

describe("isFilter", () => {
  it("accepts a well-formed chip", () => {
    expect(isFilter(product)).toBe(true);
  });

  it("rejects an unknown operator", () => {
    expect(isFilter({ ...product, op: "Like" })).toBe(false);
  });
});

describe("resolveField", () => {
  it("finds a field by alias, ignoring case", () => {
    expect(resolveField(schema, "TYPE")?.name).toBe("__NodeTypeAlias");
  });

  it("finds nothing for an unknown field", () => {
    expect(resolveField(schema, "colour")).toBeUndefined();
  });
});

describe("fieldLabel", () => {
  it("uses the alias label", () => {
    expect(fieldLabel(schema, "__NodeTypeAlias")).toBe("Document type");
  });

  it("falls back to the field name", () => {
    expect(fieldLabel(schema, "level")).toBe("level");
  });
});

describe("disabledReason", () => {
  it("lets a known field with a fitting operator run", () => {
    expect(disabledReason(level, schema, target, term)).toBeUndefined();
  });

  it("disables an unknown field", () => {
    expect(disabledReason({ ...product, field: "colour" }, schema, target, term)).toBe(
      'There is no field "colour" in ExternalIndex',
    );
  });

  it("disables an operator the field type does not have", () => {
    expect(disabledReason({ ...level, op: "Contains" }, schema, target, term)).toBe(
      '"contains" does not apply to number fields',
    );
  });

  it("disables raw Lucene on an index without a Lucene searcher", () => {
    const lucene: Filter = { kind: "Lucene", field: null, op: "Contains", value: "a:b", exclude: false };
    const other = { ...target, capabilities: { nativeQuery: false, termStatistics: false } };
    expect(disabledReason(lucene, schema, other, term)).toBe("ExternalIndex does not take raw Lucene queries");
  });

  it("disables a chip an engine rule names, with the rule's reason", () => {
    const withRules = {
      ...schema,
      unsupported: [
        { kind: "Text" as const, types: [], operators: [], exclude: true, reason: "Cannot exclude free text" },
        {
          kind: "Field" as const,
          types: ["Text" as const],
          operators: ["Phrase" as const],
          exclude: null,
          reason: "No phrases",
        },
      ],
    };
    const text: Filter = { kind: "Text", field: null, op: "Contains", value: "summer", exclude: true };
    const phrase: Filter = { kind: "Field", field: "nodeName", op: "Phrase", value: "a b", exclude: false };
    expect([
      disabledReason(text, withRules, target, term),
      disabledReason({ ...text, exclude: false }, withRules, target, term),
      disabledReason(phrase, withRules, target, term),
      disabledReason({ ...phrase, field: "__NodeTypeAlias", op: "Is" }, withRules, target, term),
    ]).toEqual(["Cannot exclude free text", undefined, "No phrases", undefined]);
  });

  it("disables nothing while the schema loads", () => {
    expect(disabledReason({ ...product, field: "colour" }, undefined, target, term)).toBeUndefined();
  });
});
