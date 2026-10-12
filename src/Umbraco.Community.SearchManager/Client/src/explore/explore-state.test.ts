import { describe, expect, it } from "vitest";
import { createExploreState, decodeExploreState, encodeExploreState, mergeIntoSearch } from "./explore-state.js";

describe("explore view state in the URL", () => {
  it("round-trips every field", () => {
    const state = {
      target: "ExternalIndex",
      culture: "da-DK",
      filters: [{ kind: "Field" as const, field: "level", op: "Equals" as const, value: "3", exclude: false }],
      columns: ["writerName", "level"],
      doc: "1234",
      page: 3,
      native: "+level:[3 TO 3]",
      showQuery: true,
    };
    expect(decodeExploreState(encodeExploreState(state))).toEqual(state);
  });

  it("keeps an empty native query as native mode", () => {
    expect(decodeExploreState(encodeExploreState({ ...createExploreState(), native: "" })).native).toBe("");
  });

  it("writes nothing for the defaults", () => {
    expect(encodeExploreState(createExploreState()).toString()).toBe("");
  });

  it("keeps non-ASCII chip values", () => {
    const state = {
      ...createExploreState(),
      filters: [{ kind: "Text" as const, field: null, op: "Contains" as const, value: "Hjem æøå", exclude: false }],
    };
    expect(decodeExploreState(encodeExploreState(state)).filters).toEqual(state.filters);
  });

  it("drops broken chips but keeps the rest of the link", () => {
    const decoded = decodeExploreState(new URLSearchParams("index=ExternalIndex&f=%%%&page=2"));
    expect(decoded).toMatchObject({ target: "ExternalIndex", filters: [], page: 2 });
  });

  it("ignores a page that is not a positive whole number", () => {
    expect(decodeExploreState(new URLSearchParams("page=-4")).page).toBe(1);
  });

  it("keeps other query-string keys when merging", () => {
    expect(mergeIntoSearch("?other=1&index=Old", new URLSearchParams("index=New"))).toBe("other=1&index=New");
  });
});
