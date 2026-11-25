import { describe, expect, it } from "vitest";
import {
  buildConcatList,
  createTimeMap,
  enableExpression,
  isFastForward,
  toWebVtt,
  vttTimestamp,
} from "./demo-timeline.js";

describe("isFastForward", () => {
  it("plays a wait of exactly the threshold in real time", () => {
    expect(isFastForward(4_000)).toBe(false);
  });

  it("fast-forwards a wait just over the threshold", () => {
    expect(isFastForward(4_001)).toBe(true);
  });
});

describe("createTimeMap", () => {
  // Keep 1 s to 21 s; a 10 s wait from 5 s to 15 s is squeezed to 2 s.
  const map = createTimeMap({ start: 1_000, end: 21_000 }, [{ start: 5_000, end: 15_000 }]);

  it("maps the load-in before the kept part to the video's start", () => {
    expect(map(0)).toBe(0);
  });

  it("plays time before a fast-forward in real time", () => {
    expect(map(5_000)).toBe(4_000);
  });

  it("runs time inside a fast-forward proportionally faster", () => {
    expect(map(10_000)).toBe(5_000);
  });

  it("shortens everything after a fast-forward by the time it saved", () => {
    expect(map(21_000)).toBe(12_000);
  });

  it("holds the video's end for anything recorded after the kept part", () => {
    expect(map(30_000)).toBe(12_000);
  });

  it("adds up the time saved by several fast-forwards", () => {
    const twoWaits = createTimeMap({ start: 0, end: 30_000 }, [
      { start: 10_000, end: 20_000 },
      { start: 2_000, end: 7_000 },
    ]);

    expect(twoWaits(30_000)).toBe(19_000);
  });

  it("squeezes only the kept part of a fast-forward that starts during the load-in", () => {
    const straddling = createTimeMap({ start: 5_000, end: 15_000 }, [{ start: 0, end: 9_000 }]);

    expect(straddling(9_000)).toBe(2_000);
  });
});

describe("buildConcatList", () => {
  const keep = { start: 1_000, end: 3_000 };
  const map = createTimeMap(keep, []);

  it("shows each frame until the next, from the frame on screen at the start to the end", () => {
    const frames = [
      { file: "a.jpg", t: 0 },
      { file: "b.jpg", t: 1_500 },
      { file: "c.jpg", t: 2_500 },
      { file: "d.jpg", t: 9_000 },
    ];

    expect(buildConcatList(frames, keep, map)).toBe(
      [
        "ffconcat version 1.0",
        "file 'a.jpg'",
        "duration 0.500",
        "file 'b.jpg'",
        "duration 1.000",
        "file 'c.jpg'",
        "duration 0.500",
        "file 'c.jpg'",
        "",
      ].join("\n"),
    );
  });

  it("escapes a single quote in a frame's file name", () => {
    const list = buildConcatList([{ file: "it's.jpg", t: 1_000 }], keep, map);

    expect(list).toContain("file 'it'\\''s.jpg'");
  });

  it("refuses a recording with no frame inside the kept part", () => {
    expect(() => buildConcatList([{ file: "late.jpg", t: 5_000 }], { start: 0, end: 1_000 }, map)).toThrow(
      "No frames were captured",
    );
  });
});

describe("vttTimestamp", () => {
  it("formats hours, minutes, seconds and milliseconds", () => {
    expect(vttTimestamp(3_723_004)).toBe("01:02:03.004");
  });
});

describe("toWebVtt", () => {
  const map = createTimeMap({ start: 1_000, end: 10_000 }, []);

  it("writes each caption as a numbered cue in video time", () => {
    const vtt = toWebVtt([{ start: 1_000, end: 2_500, text: "Every index on one page" }], map);

    expect(vtt).toBe("WEBVTT\n\n1\n00:00:00.000 --> 00:00:01.500\nEvery index on one page\n");
  });

  it("leaves out a caption shown only during the load-in", () => {
    const vtt = toWebVtt([{ start: 0, end: 500, text: "Signing in" }], map);

    expect(vtt).toBe("WEBVTT\n\n");
  });
});

describe("enableExpression", () => {
  it("is true while a fast-forward plays, in seconds of video time", () => {
    const spans = [{ start: 5_000, end: 15_000 }];
    const map = createTimeMap({ start: 1_000, end: 21_000 }, spans);

    expect(enableExpression(spans, map)).toBe("between(t,4.000,6.000)");
  });

  it("is never true when nothing was fast-forwarded", () => {
    expect(enableExpression([], createTimeMap({ start: 0, end: 1_000 }, []))).toBe("0");
  });
});
