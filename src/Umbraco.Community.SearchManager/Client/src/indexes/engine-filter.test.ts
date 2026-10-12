import { describe, expect, it } from "vitest";
import { engineOptions, filterByEngine } from "./engine-filter.js";

const indexes = [
  { name: "ExternalIndex", engine: "examine" },
  { name: "InternalIndex", engine: "examine" },
  { name: "Umb_Content", engine: "umbraco-search" },
];

describe("engineOptions", () => {
  it("counts all indexes, then each engine in the order it first appears", () => {
    expect(engineOptions(indexes)).toEqual([
      { value: "all", count: 3 },
      { value: "examine", count: 2 },
      { value: "umbraco-search", count: 1 },
    ]);
  });

  it("offers only 'all' when there are no indexes", () => {
    expect(engineOptions([])).toEqual([{ value: "all", count: 0 }]);
  });
});

describe("filterByEngine", () => {
  it("keeps only the selected engine's indexes", () => {
    expect(filterByEngine(indexes, "umbraco-search").map((index) => index.name)).toEqual(["Umb_Content"]);
  });

  it("keeps every index for 'all'", () => {
    expect(filterByEngine(indexes, "all")).toHaveLength(3);
  });

  it("shows nothing for an engine with no indexes", () => {
    expect(filterByEngine(indexes, "elastic")).toEqual([]);
  });
});
