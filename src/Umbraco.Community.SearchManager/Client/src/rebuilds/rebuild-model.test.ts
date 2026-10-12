import { describe, expect, it } from "vitest";
import type { IndexSummary, RebuildRun } from "../api/types.gen.js";
import {
  estimatePercent,
  finishedEmpty,
  formatDuration,
  hasActiveRuns,
  latestRunByIndex,
  rebuildBlockReason,
  toggleAll,
  waitPosition,
} from "./rebuild-model.js";

function run(overrides: Partial<RebuildRun>): RebuildRun {
  return {
    id: "1",
    indexName: "ExternalIndex",
    engine: "examine",
    mode: "OneAtATime",
    status: "Queued",
    triggeredBy: "Jack",
    queuedAt: "2026-10-12T09:00:00Z",
    zeroDowntime: false,
    isMemberIndex: false,
    ...overrides,
  };
}

function index(overrides: Partial<IndexSummary>): IndexSummary {
  return {
    name: "ExternalIndex",
    engine: "examine",
    health: "Healthy",
    capabilities: { rebuild: true, search: true, nativeQuery: true, termStatistics: true },
    zeroDowntime: false,
    isMemberIndex: false,
    properties: {},
    ...overrides,
  };
}

describe("latestRunByIndex", () => {
  it("keeps the newest run of each index, whatever the name's case", () => {
    const latest = latestRunByIndex([
      run({ id: "old", status: "Failed" }),
      run({ id: "new", indexName: "externalindex", status: "Succeeded" }),
    ]);
    expect(latest.get("externalindex")?.id).toBe("new");
  });
});

describe("rebuildBlockReason", () => {
  it("gives the index's own reason when it cannot rebuild", () => {
    const reason = rebuildBlockReason(
      index({
        capabilities: { rebuild: false, search: true, nativeQuery: true, termStatistics: true },
        rebuildUnavailableReason: "No populator",
      }),
      undefined,
      "busy",
    );
    expect(reason).toBe("No populator");
  });

  it("says the index is busy while a run is queued or running", () => {
    expect(rebuildBlockReason(index({}), run({ status: "Running" }), "busy")).toBe("busy");
  });

  it("allows an index whose last run finished", () => {
    expect(rebuildBlockReason(index({}), run({ status: "Failed" }), "busy")).toBeUndefined();
  });
});

describe("estimatePercent", () => {
  it("divides documents written by the expected total", () => {
    expect(estimatePercent(run({ status: "Running", documentsWritten: 1706, estimatedTotal: 3412 }))).toBe(50);
  });

  it("never reaches 100 before the server says the run is done", () => {
    expect(estimatePercent(run({ status: "Running", documentsWritten: 4000, estimatedTotal: 3412 }))).toBe(99);
  });

  it("has no estimate without a previous count", () => {
    expect(estimatePercent(run({ status: "Running", documentsWritten: 10, estimatedTotal: 0 }))).toBeNull();
  });
});

describe("finishedEmpty", () => {
  it("is true for a successful run that left the index empty", () => {
    expect(finishedEmpty(run({ status: "Succeeded", documentsAfter: 0 }))).toBe(true);
  });

  it("is false when the index holds documents, or the count is unknown", () => {
    expect([
      finishedEmpty(run({ status: "Succeeded", documentsAfter: 189 })),
      finishedEmpty(run({ status: "Succeeded", documentsAfter: null })),
      finishedEmpty(run({ status: "Failed", documentsAfter: 0 })),
    ]).toEqual([false, false, false]);
  });
});

describe("waitPosition", () => {
  it("counts only queued one-at-a-time runs ahead", () => {
    const runs = [
      run({ id: "a", status: "Running" }),
      run({ id: "b", mode: "AllAtOnce" }),
      run({ id: "c" }),
      run({ id: "d" }),
    ];
    expect(waitPosition(runs, runs[3]!)).toBe(2);
  });

  it("gives no position to an all-at-once run", () => {
    const all = run({ mode: "AllAtOnce" });
    expect(waitPosition([all], all)).toBeNull();
  });
});

describe("hasActiveRuns", () => {
  it("is false once every run has finished", () => {
    expect(hasActiveRuns([run({ status: "Succeeded" }), run({ status: "Cancelled" })])).toBe(false);
  });
});

describe("toggleAll", () => {
  it("selects every eligible index when some are not selected", () => {
    expect([...toggleAll(new Set(["A"]), ["A", "B"])].sort()).toEqual(["A", "B"]);
  });

  it("clears the eligible indexes when all are selected, keeping others", () => {
    expect([...toggleAll(new Set(["A", "B", "Hidden"]), ["A", "B"])]).toEqual(["Hidden"]);
  });
});

describe("formatDuration", () => {
  it("shows seconds under a minute", () => {
    expect(formatDuration("2026-10-12T09:00:00Z", "2026-10-12T09:00:45Z")).toBe("45 s");
  });

  it("shows minutes and seconds", () => {
    expect(formatDuration("2026-10-12T09:00:00Z", "2026-10-12T09:02:05Z")).toBe("2 min 5 s");
  });
});
