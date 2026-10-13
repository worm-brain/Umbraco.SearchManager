import { describe, expect, it } from "vitest";
import { emptyUmbracoSearchIndexes, settled } from "./umbraco-search-setup.js";

describe("emptyUmbracoSearchIndexes", () => {
  it("picks the Umbraco Search indexes with no documents", () => {
    expect(
      emptyUmbracoSearchIndexes([
        { name: "Umb_Content", engine: "umbraco-search", documentCount: 0 },
        { name: "Umb_Media", engine: "umbraco-search", documentCount: null },
        { name: "Umb_PublishedContent", engine: "umbraco-search", documentCount: 198 },
        { name: "ExternalIndex", engine: "examine", documentCount: 0 },
      ]),
    ).toEqual(["Umb_Content", "Umb_Media"]);
  });
});

describe("settled", () => {
  it("is true once every rebuilt index has a finished run", () => {
    expect(
      settled(
        [
          { indexName: "Umb_Content", status: "Succeeded" },
          { indexName: "Umb_Media", status: "Failed" },
        ],
        ["Umb_Content", "Umb_Media"],
      ),
    ).toBe(true);
  });

  it("is false while a run is queued or running, or has not appeared yet", () => {
    expect([
      settled([{ indexName: "Umb_Content", status: "Running" }], ["Umb_Content"]),
      settled([], ["Umb_Content"]),
    ]).toEqual([false, false]);
  });
});
