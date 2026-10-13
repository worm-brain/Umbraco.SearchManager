import { describe, expect, it } from "vitest";
import {
  createDiagnoseState,
  decodeDiagnoseState,
  encodeDiagnoseState,
  mergeDiagnoseIntoSearch,
  type DiagnoseViewState,
} from "./diagnose-state.js";

describe("diagnose view state in the URL", () => {
  it("round-trips every field", () => {
    const state: DiagnoseViewState = {
      tool: "node",
      index: "ExternalIndex",
      variants: true,
      filter: "node",
      node: "8e1f2a3b-4c5d-4e6f-9a0b-1c2d3e4f5a6b",
      culture: "en-US",
      q: "umbraco",
    };
    expect(decodeDiagnoseState(encodeDiagnoseState(state))).toEqual(state);
  });

  it("writes nothing for the defaults", () => {
    expect(encodeDiagnoseState(createDiagnoseState()).toString()).toBe("");
  });

  it("selects the index a Search Manager link names with ?index=", () => {
    expect(decodeDiagnoseState(new URLSearchParams("?index=Umb_Content")).index).toBe("Umb_Content");
  });

  it("opens Find a node on the node a link names, keeping its culture and query", () => {
    const decoded = decodeDiagnoseState(
      new URLSearchParams("tool=node&node=abc&culture=da-DK&q=umbraco%20type%3AblogPost"),
    );
    expect(decoded).toMatchObject({ tool: "node", node: "abc", culture: "da-DK", q: "umbraco type:blogPost" });
  });

  it("falls back to the fields tool for an unknown tool, keeping the rest", () => {
    const decoded = decodeDiagnoseState(new URLSearchParams("tool=nope&index=ExternalIndex&variants=1"));
    expect(decoded).toEqual({ ...createDiagnoseState(), index: "ExternalIndex", variants: true });
  });

  it("keeps other keys when merging into a query string", () => {
    const merged = mergeDiagnoseIntoSearch("?tool=node&other=1", encodeDiagnoseState(createDiagnoseState()));
    expect(merged).toBe("other=1");
  });
});
