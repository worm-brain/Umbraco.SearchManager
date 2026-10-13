import { describe, expect, it } from "vitest";
import en from "../lang/en.js";
import { engineLabel, formatBytes, formatCount, healthLook, incompleteDetailTerm } from "./index-format.js";

describe("formatCount", () => {
  it("groups digits", () => {
    expect(formatCount(3412)).toBe("3,412");
  });

  it("shows a dash when the source cannot count", () => {
    expect(formatCount(null)).toBe("-");
  });
});

describe("incompleteDetailTerm", () => {
  it("passes the grouped count and the error", () => {
    expect(
      incompleteDetailTerm({ endedAt: "2026-10-13T09:00:00Z", documentsWritten: 1204, error: "Price missing" }),
    ).toEqual(["searchManager_healthIncompleteDetail", "1,204", "Price missing"]);
  });

  it("passes empty strings for an unknown count and error", () => {
    expect(incompleteDetailTerm({ endedAt: "2026-10-13T09:00:00Z" })).toEqual([
      "searchManager_healthIncompleteDetail",
      "",
      "",
    ]);
  });
});

describe("healthIncompleteDetail", () => {
  const term = en.searchManager.healthIncompleteDetail;

  it("says how many documents were written and why it failed", () => {
    expect(term("1,204", "Price missing")).toBe("Last rebuild failed after writing 1,204 documents: Price missing");
  });

  it("leaves out an unknown count and error", () => {
    expect(term("", "")).toBe("Last rebuild failed part-way");
  });
});

describe("formatBytes", () => {
  it("uses binary units with one decimal", () => {
    expect(formatBytes(40_055_603)).toBe("38.2 MB");
  });

  it("keeps whole bytes under a kilobyte", () => {
    expect(formatBytes(512)).toBe("512 B");
  });

  it("shows a dash when the size is unknown", () => {
    expect(formatBytes(undefined)).toBe("-");
  });
});

describe("healthLook", () => {
  it("draws a healthy index with the positive colour", () => {
    expect(healthLook("Healthy")).toEqual({ icon: "icon-check", color: "var(--uui-color-positive)" });
  });

  it("draws a corrupted index with the danger colour", () => {
    expect(healthLook("Corrupted").color).toBe("var(--uui-color-danger)");
  });
});

describe("engineLabel", () => {
  it("names Umbraco Search", () => {
    expect(engineLabel("umbraco-search")).toBe("Umbraco Search");
  });

  it("falls back to the id for an unknown engine", () => {
    expect(engineLabel("elastic")).toBe("elastic");
  });
});
