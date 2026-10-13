import { describe, expect, it } from "vitest";
import type { ExplainNode, Filter } from "../api/index.js";
import {
  compareExplanations,
  describeContribution,
  describeMustNotMatch,
  describeNode,
  explainUnavailableReason,
  flattenContributions,
  formatDifference,
  formatScore,
  type ExplainContext,
} from "./score-explain.js";
import { schema, target, term } from "./term.test-helper.js";

const node = (partial: Partial<ExplainNode> & Pick<ExplainNode, "kind" | "value">): ExplainNode => ({
  description: "",
  children: [],
  ...partial,
});

const word = (field: string, text: string, value: number): ExplainNode =>
  node({ kind: "TermMatch", value, field, term: text, factors: { freq: 2, tf: 1.41, idf: 4.21, queryNorm: 0.2 } });

const typeChip: Filter = { kind: "Field", field: "__NodeTypeAlias", op: "Is", value: "product", exclude: false };

const context: ExplainContext = { filters: [typeChip], schema, term };

/** `(nodeName:home bodyText:home) +__NodeTypeAlias:product`, one of two words matched. */
const tree = node({
  kind: "Sum",
  value: 1.5,
  children: [
    node({
      kind: "Coord",
      value: 0.5,
      factors: { matched: 1, of: 2 },
      children: [node({ kind: "Sum", value: 1, children: [word("nodeName", "home", 1)] })],
    }),
    node({
      kind: "Filter",
      value: 1,
      field: "__NodeTypeAlias",
      term: "product",
      filterIndex: 0,
      children: [word("__NodeTypeAlias", "product", 1)],
    }),
  ],
});

describe("flattenContributions", () => {
  it("gives one row per word and filter, a coord row and the query norm", () => {
    const rows = flattenContributions(tree);
    expect(rows.map((row) => [row.kind, row.field, row.value])).toEqual([
      ["TermMatch", "nodeName", 1],
      ["Coord", null, -0.5],
      ["Filter", "__NodeTypeAlias", 1],
      ["QueryNorm", null, 0.2],
    ]);
  });

  it("adds up to the score, leaving out the query norm", () => {
    const total = flattenContributions(tree)
      .filter((row) => row.kind !== "QueryNorm")
      .reduce((sum, row) => sum + row.value, 0);
    expect(total).toBeCloseTo(tree.value);
  });

  it("sums a word that occurs twice", () => {
    const twice = node({ kind: "Sum", value: 3, children: [word("nodeName", "home", 1), word("nodeName", "home", 2)] });
    expect(flattenContributions(twice).find((row) => row.kind === "TermMatch")?.value).toBe(3);
  });

  it("gives no rows without a tree", () => {
    expect(flattenContributions(undefined)).toEqual([]);
  });

  it("leaves out clauses the document does not meet", () => {
    const missed = node({
      kind: "Sum",
      value: 1,
      children: [word("nodeName", "home", 1), node({ kind: "NoMatch", value: 0 })],
    });
    expect(flattenContributions(missed).map((row) => row.kind)).toEqual(["TermMatch", "QueryNorm"]);
  });
});

describe("compareExplanations", () => {
  const a = flattenContributions(tree);
  const b = flattenContributions(node({ kind: "Sum", value: 2, children: [word("bodyText", "home", 2)] }));

  it("joins by key, counting a missing row as 0, largest difference first", () => {
    const rows = compareExplanations(a, b, false);
    expect(rows.map((row) => [row.kind, row.field, row.a, row.b, row.difference])).toEqual([
      ["TermMatch", "bodyText", 0, 2, -2],
      ["TermMatch", "nodeName", 1, 0, 1],
      ["Filter", "__NodeTypeAlias", 1, 0, 1],
      ["Coord", null, -0.5, 0, -0.5],
      ["QueryNorm", null, 0.2, 0.2, 0],
    ]);
  });

  it("drops equal rows with differences only", () => {
    expect(compareExplanations(a, b, true).some((row) => row.kind === "QueryNorm")).toBe(false);
  });

  it("gives nothing for two identical scores with differences only", () => {
    expect(compareExplanations(a, a, true)).toEqual([]);
  });
});

describe("describeNode", () => {
  it("reads a word match with its figures", () => {
    const sentence = describeNode(
      node({
        kind: "TermMatch",
        value: 1,
        field: "nodeName",
        term: "home",
        factors: { freq: 2, tf: 1.41421, idf: 4.2133, fieldNorm: 0.3125, docFreq: 3, maxDocs: 120 },
      }),
      context,
    );
    expect(sentence).toEqual({
      text: 'nodeName contains "home" 2 times',
      detail: "tf 1.41 · idf 4.21 · field norm 0.313 · in 3 of 120 documents",
    });
  });

  it("reads a trailing star as a prefix", () => {
    const sentence = describeNode(node({ kind: "Constant", value: 1, field: "nodeName", term: "hood*" }), context);
    expect(sentence.text).toBe('nodeName starts with "hood" (every match scores the same)');
  });

  it("names a filter by the chip it came from", () => {
    const filter = node({ kind: "Filter", value: 1, field: "__NodeTypeAlias", term: "product", filterIndex: 0 });
    expect(describeNode(filter, context).text).toBe("must match: Document type is product");
  });

  it("falls back to field and term for a filter with no chip", () => {
    const filter = node({ kind: "Filter", value: 1, field: "level", term: "[3 TO 3]", filterIndex: null });
    expect(describeNode(filter, context).text).toBe("must match: level:[3 TO 3]");
  });

  it("shows the server's description for a clause the document does not meet", () => {
    expect(describeNode(node({ kind: "NoMatch", value: 0, description: "no nodeName:home" }), context).text).toBe(
      "no nodeName:home",
    );
  });
});

describe("describeMustNotMatch", () => {
  it("reads an excluded chip as must not match", () => {
    const excluded: ExplainContext = { ...context, filters: [{ ...typeChip, exclude: true }] };
    expect(describeMustNotMatch(0, excluded)).toBe("must not match Document type is product, adds no score");
  });

  it("gives nothing for an index past the sent chips", () => {
    expect(describeMustNotMatch(4, context)).toBeUndefined();
  });
});

describe("describeContribution", () => {
  it("reads a word row without the per-document figures", () => {
    const [row] = flattenContributions(word("nodeName", "home", 1));
    expect(describeContribution(row!, context)).toBe('nodeName contains "home"');
  });
});

describe("formatScore", () => {
  it("rounds to three significant digits", () => {
    expect([formatScore(4.21337), formatScore(0.012345), formatScore(1234.5)]).toEqual(["4.21", "0.0123", "1230"]);
  });

  it("gives an empty string for no value", () => {
    expect(formatScore(null)).toBe("");
  });

  it("signs a positive difference", () => {
    expect([formatDifference(0.42), formatDifference(-1.3), formatDifference(0)]).toEqual(["+0.42", "-1.3", "0"]);
  });
});

describe("explainUnavailableReason", () => {
  it("gives no reason for a Lucene index", () => {
    expect(explainUnavailableReason(target, term)).toBeUndefined();
  });

  it("says a non-Lucene index cannot explain scores", () => {
    const other = { ...target, capabilities: { ...target.capabilities, explainScore: false } };
    expect(explainUnavailableReason(other, term)).toBe(
      "ExternalIndex is not a Lucene index, so its scores cannot be explained",
    );
  });

  it("says Umbraco Search returns no scores", () => {
    const umbracoSearch = {
      ...target,
      name: "Umb_Content",
      engine: "umbraco-search",
      capabilities: { ...target.capabilities, explainScore: false },
    };
    expect(explainUnavailableReason(umbracoSearch, term)).toBe("Umbraco Search does not return scores");
  });
});
