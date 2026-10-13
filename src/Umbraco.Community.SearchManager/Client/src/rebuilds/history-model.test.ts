import { describe, expect, it } from "vitest";
import type { RebuildHistoryEntry } from "../api/types.gen.js";
import { agoParts, historyTriggerTerm, latestEntryByIndex } from "./history-model.js";

const now = new Date("2026-10-12T12:00:00Z");

function entry(indexName: string, startedAt: string): RebuildHistoryEntry {
  return {
    indexName,
    engine: "examine",
    server: "WEB01",
    trigger: "User",
    triggeredBy: "Jack",
    startedAt,
    endedAt: startedAt,
    outcome: "Succeeded",
    isMemberIndex: false,
  };
}

describe("agoParts", () => {
  it("gives whole days for a run days ago", () => {
    expect(agoParts("2026-10-10T09:00:00Z", now)).toEqual({ value: -2, unit: "day" });
  });

  it("gives minutes within the hour", () => {
    expect(agoParts("2026-10-12T11:35:00Z", now)).toEqual({ value: -25, unit: "minute" });
  });

  it("gives seconds for a run that just ended, never a future time", () => {
    expect(agoParts("2026-10-12T12:00:05Z", now)).toEqual({ value: 0, unit: "second" });
  });
});

describe("historyTriggerTerm", () => {
  it("names the user of a Search Manager rebuild", () => {
    expect(historyTriggerTerm({ trigger: "User", triggeredBy: "Jack" })).toEqual([
      "searchManager_historyTriggerUser",
      "Jack",
    ]);
  });

  it("labels Umbraco's startup rebuild", () => {
    expect(historyTriggerTerm({ trigger: "Startup" })).toEqual(["searchManager_historyTriggerStartup"]);
  });

  it("labels any other rebuild as started outside Search Manager", () => {
    expect(historyTriggerTerm({ trigger: "Other" })).toEqual(["searchManager_historyTriggerOther"]);
  });

  it("falls back to outside Search Manager for a user entry with no name", () => {
    expect(historyTriggerTerm({ trigger: "User", triggeredBy: null })).toEqual(["searchManager_historyTriggerOther"]);
  });
});

describe("latestEntryByIndex", () => {
  it("keys each entry by its index name, case-insensitively", () => {
    const latest = latestEntryByIndex([entry("ProductIndex", "2026-10-12T09:00:00Z")]);

    expect(latest.get("productindex")?.indexName).toBe("ProductIndex");
  });
});
