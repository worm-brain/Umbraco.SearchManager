import { describe, expect, it } from "vitest";
import en from "./en.js";

const strings = en.searchManager;

describe("English counts", () => {
  it("uses the singular for exactly one", () => {
    expect([
      strings.resultsCount(1),
      strings.resultsWhole(1),
      strings.fieldsWholeIndex(1),
      strings.fieldsResults(1),
      strings.rebuildsRunningNoEstimate(1),
      strings.rebuildsSucceeded("2s", 1),
      strings.rebuildsRunning(1, 1, 100),
      strings.explainDocFreq("1", "1"),
    ]).toEqual([
      "1 result",
      "1 document, newest first",
      "whole index, 1 document",
      "this 1 result",
      "1 document so far",
      "Done in 2s, 1 document",
      "About 1 of 1 document (100%), estimated from the last run",
      "in 1 of 1 document",
    ]);
  });

  it("uses the plural, grouped, for more than one", () => {
    expect([
      strings.resultsWhole(2104),
      strings.fieldsWholeIndex(2),
      strings.fieldsResults(2104),
      strings.rebuildsSucceeded("2s", 0),
      strings.rebuildsRunning(5, 120, 4),
    ]).toEqual([
      "2,104 documents, newest first",
      "whole index, 2 documents",
      "these 2,104 results",
      "Done in 2s, 0 documents",
      "About 5 of 120 documents (4%), estimated from the last run",
    ]);
  });

  it("reads an unknown count as a dash", () => {
    expect(strings.rebuildsRunningNoEstimate(null)).toBe("- documents so far");
  });
});
