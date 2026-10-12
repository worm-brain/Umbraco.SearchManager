import { describe, expect, it } from "vitest";
import type { RebuildHistoryEntry } from "../api/types.gen.js";
import { agoParts, latestEntryByIndex } from "./history-model.js";

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

describe("latestEntryByIndex", () => {
  it("keys each entry by its index name, case-insensitively", () => {
    const latest = latestEntryByIndex([entry("ProductIndex", "2026-10-12T09:00:00Z")]);

    expect(latest.get("productindex")?.indexName).toBe("ProductIndex");
  });
});
