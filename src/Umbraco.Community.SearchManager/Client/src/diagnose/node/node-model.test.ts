import { describe, expect, it } from "vitest";
import type { Filter, IndexPresence, NodeMatchResult } from "../../api/index.js";
import {
  clauseLook,
  clauseOutcome,
  cultureStateKey,
  culturesText,
  matchableIndexes,
  matchTarget,
  pairClauses,
  presenceLook,
  verdictLook,
  viewFieldsLink,
} from "./node-model.js";

/** One index's presence; present in ExternalIndex by default. */
function presence(overrides: Partial<IndexPresence> = {}): IndexPresence {
  return {
    target: "ExternalIndex",
    engine: "examine",
    status: "Present",
    documentCount: 1,
    cultures: [],
    ...overrides,
  };
}

const chip: Filter = { kind: "Field", field: "__NodeId", op: "Is", value: "1098", exclude: false };

describe("presenceLook", () => {
  it("gives every status its own icon and label", () => {
    const looks = (["Present", "Missing", "NotApplicable"] as const).map(presenceLook);
    expect(looks.map((look) => [look.icon, look.label])).toEqual([
      ["icon-check", "searchManager_nodePresent"],
      ["icon-alert", "searchManager_nodeMissing"],
      ["icon-block", "searchManager_nodeNotApplicable"],
    ]);
  });
});

describe("matchableIndexes and matchTarget", () => {
  const indexes = [
    presence({ target: "MembersIndex", status: "NotApplicable" }),
    presence({ target: "ExternalIndex", status: "Missing" }),
    presence({ target: "InternalIndex" }),
    presence({ target: "Umb_Content", engine: "umbraco-search" }),
  ];

  it("keeps only the indexes that hold the node", () => {
    expect(matchableIndexes(indexes).map((index) => index.target)).toEqual(["InternalIndex", "Umb_Content"]);
  });

  it("uses the picked index when it holds the node", () => {
    expect(matchTarget(indexes, "Umb_Content")).toBe("Umb_Content");
  });

  it("falls back to the first index that holds the node", () => {
    expect(matchTarget(indexes, "ExternalIndex")).toBe("InternalIndex");
  });

  it("has no target when no index holds the node", () => {
    expect(matchTarget([presence({ status: "Missing" })], undefined)).toBeUndefined();
  });
});

describe("viewFieldsLink", () => {
  it("opens Explore with the server's chip, document and culture", () => {
    const link = viewFieldsLink(
      presence({ target: "Umb_Content", viewFilter: chip, viewDocumentId: "abc", viewCulture: "en-US" }),
    );
    expect(link).toEqual({ target: "Umb_Content", filters: [chip], doc: "abc", culture: "en-US" });
  });

  it("gives no link for a missing row, even with a chip", () => {
    expect(viewFieldsLink(presence({ status: "Missing", viewFilter: chip }))).toBeUndefined();
  });

  it("gives no link without a chip", () => {
    expect(viewFieldsLink(presence())).toBeUndefined();
  });
});

describe("cultures", () => {
  it("joins an index's cultures", () => {
    expect(culturesText(["en-us", "da-dk"])).toBe("en-us, da-dk");
  });

  it("tells published, published with changes and unpublished cultures apart", () => {
    expect(
      [
        { iso: "en-US", published: true, draft: false },
        { iso: "da-DK", published: true, draft: true },
        { iso: "de-DE", published: false, draft: true },
      ].map(cultureStateKey),
    ).toEqual([
      "searchManager_nodeCulturePublished",
      "searchManager_nodeCultureDraft",
      "searchManager_nodeCultureUnpublished",
    ]);
  });
});

describe("match results", () => {
  const result = (overrides: Partial<NodeMatchResult>): NodeMatchResult => ({
    inIndex: true,
    matches: true,
    query: "",
    clauses: [],
    ...overrides,
  });

  it("words the overall verdict", () => {
    expect(
      [result({}), result({ matches: false }), result({ inIndex: false, matches: false })].map(
        (value) => verdictLook(value).label,
      ),
    ).toEqual(["searchManager_matchYes", "searchManager_matchNo", "searchManager_matchNotInIndex"]);
  });

  it("reads a chip's outcome, null being skipped", () => {
    expect([true, false, null].map((matched) => clauseOutcome({ index: 0, matched, nodeValues: [] }))).toEqual([
      "matched",
      "notMatched",
      "skipped",
    ]);
  });

  it("treats a chip the server did not answer for as skipped", () => {
    expect(clauseLook(clauseOutcome(undefined)).label).toBe("searchManager_clauseSkipped");
  });

  it("pairs chips with their results by position", () => {
    const text: Filter = { kind: "Text", field: null, op: "Contains", value: "umbraco", exclude: false };
    const paired = pairClauses(
      [text, chip],
      result({ clauses: [{ index: 1, matched: false, field: "__NodeId", nodeValues: ["1099"] }] }),
    );
    expect(paired.map((entry) => entry.clause?.nodeValues)).toEqual([undefined, ["1099"]]);
  });
});
