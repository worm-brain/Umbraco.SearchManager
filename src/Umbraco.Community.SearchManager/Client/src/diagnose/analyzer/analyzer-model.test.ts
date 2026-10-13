import { describe, expect, it } from "vitest";
import type {
  AnalysisResult,
  AnalysisToken,
  AnalyzerField,
  AnalyzerFieldsResult,
  SearchTermPart,
} from "../../api/index.js";
import { term } from "../../explore/term.test-helper.js";
import {
  analyzerField,
  analyzerInputProblem,
  analyzerSides,
  fieldDisplay,
  filterFields,
  MAX_ANALYZER_TERM,
  MAX_ANALYZER_TEXT,
  positionGaps,
  searchSentence,
  tokenLook,
  verdictView,
} from "./analyzer-model.js";

/** A token at a position, kept and unmatched by default. */
function token(text: string, position: number, overrides: Partial<AnalysisToken> = {}): AnalysisToken {
  return { text, position, startOffset: 0, endOffset: text.length, state: "Kept", match: "None", ...overrides };
}

/** A search part. */
function part(kind: SearchTermPart["kind"], text: string, group: number, required: boolean): SearchTermPart {
  return { kind, text, group, required };
}

/** A field of the Field group. */
function field(name: string, label?: string): AnalyzerField {
  return { name, label, type: "Text", group: "Field", analyzer: "StandardAnalyzer" };
}

/** ExternalIndex's analyzable fields, nodeName the default. */
const fields: AnalyzerFieldsResult = {
  target: { name: "ExternalIndex", engine: "examine", kind: "Index" } as AnalyzerFieldsResult["target"],
  available: true,
  defaultField: "nodeName",
  fields: [field("bodyText", "Body text"), field("nodeName"), field("title", "Title")],
};

/** An analysis of a text with a search result. */
function analysis(overrides: Partial<AnalysisResult> = {}): AnalysisResult {
  return {
    target: fields.target,
    available: true,
    field: "nodeName",
    stagesShown: true,
    stages: [],
    truncated: false,
    ...overrides,
  };
}

describe("analyzerSides", () => {
  it("compares ExternalIndex with InternalIndex by default", () => {
    expect(analyzerSides(["InternalIndex", "ExternalIndex", "MembersIndex"])).toEqual({
      left: "ExternalIndex",
      right: "InternalIndex",
    });
  });

  it("keeps the indexes the URL names, matching their case to the list", () => {
    expect(analyzerSides(["ExternalIndex", "Umb_Content"], { left: "umb_content", right: "externalindex" })).toEqual({
      left: "Umb_Content",
      right: "ExternalIndex",
    });
  });

  it("falls back to the first two indexes when the defaults are not there", () => {
    expect(analyzerSides(["Umb_Content", "Umb_Media"], { left: "Gone" })).toEqual({
      left: "Umb_Content",
      right: "Umb_Media",
    });
  });

  it("shows the only index in both columns", () => {
    expect(analyzerSides(["ExternalIndex"])).toEqual({ left: "ExternalIndex", right: "ExternalIndex" });
  });

  it("has nothing to show without indexes", () => {
    expect(analyzerSides([])).toEqual({ left: undefined, right: undefined });
  });
});

describe("analyzerField", () => {
  it("keeps the field the URL names when the index has it", () => {
    expect(analyzerField(fields, "title")).toBe("title");
  });

  it("uses the server's default field when the URL names a field the index lacks", () => {
    expect(analyzerField(fields, "nope")).toBe("nodeName");
  });

  it("uses the first field when there is no usable default", () => {
    expect(analyzerField({ ...fields, defaultField: "gone" }, undefined)).toBe("bodyText");
  });

  it("has no field for an index without fields", () => {
    expect(analyzerField({ ...fields, defaultField: null, fields: [] }, "title")).toBeUndefined();
  });
});

describe("fieldDisplay and filterFields", () => {
  it("shows the label with the name when there is one", () => {
    expect([fieldDisplay(field("title", "Title")), fieldDisplay(field("nodeName"))]).toEqual([
      "Title (title)",
      "nodeName",
    ]);
  });

  it("filters on the name or the label, ignoring case, in the server's order", () => {
    expect(filterFields(fields.fields, "TEXT").map((f) => f.name)).toEqual(["bodyText"]);
    expect(filterFields(fields.fields, "e").map((f) => f.name)).toEqual(["bodyText", "nodeName", "title"]);
  });

  it("finds nothing for text no field has", () => {
    expect(filterFields(fields.fields, "zzz")).toEqual([]);
  });
});

describe("analyzerInputProblem", () => {
  it("lets ordinary text and a search through", () => {
    expect(analyzerInputProblem("Opening hours", "opening")).toBeUndefined();
  });

  it("asks for text instead of sending empty text", () => {
    expect(analyzerInputProblem("   ", "opening")).toEqual(["searchManager_analyzerNoText"]);
  });

  it("refuses text and searches over the server's limits", () => {
    expect(analyzerInputProblem("a".repeat(MAX_ANALYZER_TEXT + 1), "")).toEqual([
      "searchManager_analyzerTextTooLong",
      String(MAX_ANALYZER_TEXT),
    ]);
    expect(analyzerInputProblem("text", "a".repeat(MAX_ANALYZER_TERM + 1))).toEqual([
      "searchManager_analyzerTermTooLong",
      String(MAX_ANALYZER_TERM),
    ]);
  });
});

describe("tokenLook", () => {
  it("strikes a removed token through and says so", () => {
    expect(tokenLook(token("the", 0, { state: "Removed" }), false)).toEqual({
      className: "token removed",
      struck: true,
      marked: false,
      labels: ["searchManager_analyzerTokenRemoved"],
    });
  });

  it("marks a changed token without striking it", () => {
    expect(tokenLook(token("opening", 0, { state: "Changed" }), false)).toMatchObject({
      className: "token changed",
      struck: false,
      labels: ["searchManager_analyzerTokenChanged"],
    });
  });

  it("highlights a final-stage match, with starts-with wording for a prefix", () => {
    expect(tokenLook(token("openings", 0, { match: "Prefix" }), true)).toEqual({
      className: "token match",
      struck: false,
      marked: true,
      labels: ["searchManager_analyzerTokenPrefix"],
    });
  });

  it("does not highlight a match outside the final stage", () => {
    expect(tokenLook(token("hours", 1, { match: "Exact" }), false).marked).toBe(false);
  });
});

describe("positionGaps", () => {
  it("shows the gap a removed stop word leaves", () => {
    const tokens = [token("opening", 0), token("the", 1, { state: "Removed" }), token("hours", 2)];
    expect(positionGaps(tokens)).toEqual([0, 0, 1]);
  });

  it("counts a leading gap and ignores tokens on the same position", () => {
    expect(positionGaps([token("opening", 1), token("open", 1, { state: "Added" }), token("hours", 2)])).toEqual([
      1, 0, 0,
    ]);
  });
});

describe("searchSentence", () => {
  it("reads core Examine's search for two words", () => {
    const parts = [
      part("Phrase", "opening hours", 0, false),
      part("Word", "opening", 1, true),
      part("Prefix", "opening", 1, true),
      part("Word", "hours", 2, true),
      part("Prefix", "hours", 2, true),
    ];
    expect(searchSentence(parts, term)).toBe(
      'The search becomes: opening (or words starting with opening) and hours (or words starting with hours); the exact phrase "opening hours" ranks higher.',
    );
  });

  it("leaves out a ranking phrase that repeats the one required word", () => {
    const parts = [part("Word", "cafe", 0, false), part("Word", "cafe", 1, true), part("Prefix", "cafe", 1, true)];
    expect(searchSentence(parts, term)).toBe("The search becomes: cafe (or words starting with cafe).");
  });

  it("joins groups with or when none is required", () => {
    const parts = [part("Word", "opening", 0, false), part("Fuzzy", "hours", 1, false)];
    expect(searchSentence(parts, term)).toBe("The search becomes: opening or words similar to hours.");
  });

  it("reads a single required word plainly", () => {
    expect(searchSentence([part("Word", "umbraco", 0, true)], term)).toBe("The search becomes: umbraco.");
  });

  it("says nothing without parts", () => {
    expect(searchSentence([], term)).toBeUndefined();
  });
});

describe("verdictView", () => {
  it("prompts for a search when none was typed", () => {
    expect(verdictView(analysis({ search: { matches: true, parts: [], notes: [] } }), " ")).toEqual({
      kind: "prompt",
    });
  });

  it("shows the server's verdict as text and icon", () => {
    expect(verdictView(analysis({ search: { matches: false, parts: [], notes: [] } }), "x")).toMatchObject({
      kind: "verdict",
      matches: false,
      look: { icon: "icon-wrong", label: "searchManager_analyzerNoMatch" },
    });
  });

  it("gives the reason when this field's searches are not judged", () => {
    expect(verdictView(analysis({ searchUnavailableReason: "Keyword fields match whole values." }), "x")).toEqual({
      kind: "unavailable",
      reason: "Keyword fields match whole values.",
    });
  });
});
