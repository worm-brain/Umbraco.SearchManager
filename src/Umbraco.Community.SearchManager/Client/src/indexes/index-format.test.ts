import { describe, expect, it } from "vitest";
import { engineLabel, formatBytes, formatCount, healthLook } from "./index-format.js";

describe("formatCount", () => {
  it("groups digits", () => {
    expect(formatCount(3412)).toBe("3,412");
  });

  it("shows a dash when the source cannot count", () => {
    expect(formatCount(null)).toBe("-");
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
