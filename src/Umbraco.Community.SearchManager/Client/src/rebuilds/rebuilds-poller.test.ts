import { describe, expect, it } from "vitest";
import type { RebuildRun } from "../api/types.gen.js";
import { RebuildsPoller, type PollTimers } from "./rebuilds-poller.js";

function run(status: RebuildRun["status"]): RebuildRun {
  return {
    id: "1",
    indexName: "ExternalIndex",
    engine: "examine",
    mode: "OneAtATime",
    status,
    triggeredBy: "Jack",
    queuedAt: "2026-10-12T09:00:00Z",
    zeroDowntime: false,
    isMemberIndex: false,
  };
}

/** Timers the test fires by hand. */
function manualTimers() {
  const pending: Array<() => void> = [];
  const timers: PollTimers = {
    set: (callback) => pending.push(callback),
    clear: () => pending.splice(0),
  };
  return { timers, pending };
}

describe("RebuildsPoller", () => {
  it("polls again while a run is active", async () => {
    const { timers, pending } = manualTimers();
    const poller = new RebuildsPoller(
      async () => ({ data: [run("Running")] }),
      () => {},
      2000,
      timers,
    );

    await poller.refresh();

    expect(pending).toHaveLength(1);
  });

  it("stops polling once every run has finished", async () => {
    const { timers, pending } = manualTimers();
    const poller = new RebuildsPoller(
      async () => ({ data: [run("Succeeded")] }),
      () => {},
      2000,
      timers,
    );

    await poller.refresh();

    expect(pending).toHaveLength(0);
  });

  it("hands each answer to onRuns", async () => {
    const seen: RebuildRun[][] = [];
    const poller = new RebuildsPoller(
      async () => ({ data: [run("Queued")] }),
      (runs) => seen.push(runs),
      2000,
      manualTimers().timers,
    );

    await poller.refresh();

    expect(seen[0]?.[0]?.status).toBe("Queued");
  });

  it("keeps trying after a failed request", async () => {
    const { timers, pending } = manualTimers();
    const poller = new RebuildsPoller(
      async () => ({ error: "boom" }),
      () => {},
      2000,
      timers,
    );

    await poller.refresh();

    expect(pending).toHaveLength(1);
  });

  it("does not poll after stop", async () => {
    const { timers, pending } = manualTimers();
    const poller = new RebuildsPoller(
      async () => ({ data: [run("Running")] }),
      () => {},
      2000,
      timers,
    );
    await poller.refresh();

    poller.stop();

    expect(pending).toHaveLength(0);
  });

  it("does not poll while live events are on, but still fetches when asked", async () => {
    const { timers, pending } = manualTimers();
    let fetches = 0;
    const poller = new RebuildsPoller(
      async () => {
        fetches++;
        return { data: [run("Running")] };
      },
      () => {},
      2000,
      timers,
    );
    poller.setPolling(false);

    await poller.refresh();

    expect([fetches, pending.length]).toEqual([1, 0]);
  });

  it("polls again when live events drop", async () => {
    const { timers, pending } = manualTimers();
    const poller = new RebuildsPoller(
      async () => ({ data: [run("Running")] }),
      () => {},
      2000,
      timers,
    );
    poller.setPolling(false);
    await poller.refresh();

    await poller.setPolling(true);

    expect(pending).toHaveLength(1);
  });

  it("folds refreshes asked for during a fetch into one more fetch", async () => {
    let fetches = 0;
    let release: () => void = () => {};
    const poller = new RebuildsPoller(
      () => {
        fetches++;
        return new Promise((resolve) => {
          release = () => resolve({ data: [run("Succeeded")] });
        });
      },
      () => {},
      2000,
      manualTimers().timers,
    );

    const first = poller.refresh();
    void poller.refresh();
    void poller.refresh();
    release();
    await first;
    release();
    await Promise.resolve();

    expect(fetches).toBe(2);
  });
});
