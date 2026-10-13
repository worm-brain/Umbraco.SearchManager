import { describe, expect, it } from "vitest";
import {
  createExploreState,
  decodeExploreState,
  effectiveCulture,
  encodeExploreState,
  mergeIntoSearch,
} from "./explore-state.js";
import { schema } from "./term.test-helper.js";

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
      explain: true,
      compare: ["1234", "1250"] as [string, string],
    };
    expect(decodeExploreState(encodeExploreState(state))).toEqual(state);
  });

  it("selects the index a Search Manager link names with ?index=", () => {
    expect(decodeExploreState(new URLSearchParams("?index=Umb_PublishedContent")).target).toBe("Umb_PublishedContent");
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

  it("keeps the explanation and the comparison open through the URL", () => {
    const encoded = encodeExploreState({ ...createExploreState(), explain: true, compare: ["10", "20"] });
    expect(encoded.toString()).toBe("ex=1&cmp=10%2C20");
    expect(decodeExploreState(encoded)).toMatchObject({ explain: true, compare: ["10", "20"] });
  });

  it("ignores a comparison that is not two different ids", () => {
    const decoded = ["cmp=10", "cmp=10,20,30", "cmp=10,10", "cmp=,20"].map(
      (search) => decodeExploreState(new URLSearchParams(search)).compare,
    );
    expect(decoded).toEqual([undefined, undefined, undefined, undefined]);
  });

  it.each([
    ["the default culture", undefined, ""],
    ["explicitly no culture", null, "culture="],
    ["a culture", "da-DK", "culture=da-DK"],
  ])("round-trips %s", (_name, culture, encoded) => {
    const params = encodeExploreState({ ...createExploreState(), culture });
    expect(params.toString()).toBe(encoded);
    expect(decodeExploreState(params).culture).toBe(culture);
  });

  it("keeps other query-string keys when merging", () => {
    expect(mergeIntoSearch("?other=1&index=Old", new URLSearchParams("index=New"))).toBe("other=1&index=New");
  });
});

describe("effectiveCulture", () => {
  const withDefault = { ...schema, defaultCulture: "en-US" };

  it("uses the schema's default when no culture was picked", () => {
    expect(effectiveCulture(undefined, withDefault)).toBe("en-US");
  });

  it("uses no culture when none was picked explicitly", () => {
    expect(effectiveCulture(null, withDefault)).toBeUndefined();
  });

  it("uses the picked culture", () => {
    expect(effectiveCulture("da-DK", withDefault)).toBe("da-DK");
  });

  it("uses no culture while the schema is unknown or has no default", () => {
    expect([effectiveCulture(undefined, undefined), effectiveCulture(undefined, schema)]).toEqual([
      undefined,
      undefined,
    ]);
  });
});
