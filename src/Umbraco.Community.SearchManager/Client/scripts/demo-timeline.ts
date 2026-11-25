/**
 * The timeline a demo recording writes and the renderer reads, plus the pure time arithmetic between them.
 * Identical in every package repo (package standard, demo videos): nothing here names this package.
 *
 * All times are milliseconds on one clock (`Date.now()` in the recording process). The recording keeps
 * every frame; the renderer cuts the load-in before `start`, drops everything after `end`, and squeezes each
 * fast-forward segment into {@link FAST_FORWARD_OUTPUT_MS}, all by remapping frame times through
 * {@link createTimeMap}.
 */

/** A wait at or under this length plays in real time; seeing progress is part of the story. */
export const FAST_FORWARD_THRESHOLD_MS = 4_000;

/** How long a fast-forwarded wait lasts in the finished video. */
export const FAST_FORWARD_OUTPUT_MS = 2_000;

/** One captured frame: a JPEG file (relative to the timeline file) and when it was painted. */
export interface DemoFrame {
  file: string;
  t: number;
}

/** A span of recording time, `start` inclusive, `end` exclusive. */
export interface DemoSpan {
  start: number;
  end: number;
}

/** A caption that was on screen for a span. */
export interface DemoCaption extends DemoSpan {
  text: string;
}

/** Everything one recording produced, written as `timeline.json` next to its frames. */
export interface DemoTimeline {
  /** The video's name: `overview`, or a feature clip's slug. */
  video: string;
  /** Frame size in pixels. */
  size: { width: number; height: number };
  /** The part of the recording that becomes the video: the title card's start to the end card's end. */
  keep: DemoSpan;
  frames: Array<DemoFrame>;
  captions: Array<DemoCaption>;
  /** Waits longer than {@link FAST_FORWARD_THRESHOLD_MS}, squeezed to {@link FAST_FORWARD_OUTPUT_MS}. */
  fastForwards: Array<DemoSpan>;
  /** The recording time of the frame to use as the poster image, if the spec marked one. */
  poster?: number;
}

/**
 * Says whether a wait is long enough to fast-forward.
 *
 * @param durationMs - How long the wait took.
 * @returns True when it should be squeezed rather than played in real time.
 */
export function isFastForward(durationMs: number): boolean {
  return durationMs > FAST_FORWARD_THRESHOLD_MS;
}

/**
 * Builds the function that maps recording time to video time.
 *
 * Time before `keep.start` maps to 0, time after `keep.end` to the video's end. Inside a fast-forward
 * segment, time runs linearly faster so the whole segment lasts {@link FAST_FORWARD_OUTPUT_MS}; segments
 * outside `keep` are ignored.
 *
 * @param keep - The recording span the video shows.
 * @param fastForwards - Segments to squeeze; they must not overlap.
 * @returns A monotonic function from recording time to video time, both in milliseconds.
 */
export function createTimeMap(keep: DemoSpan, fastForwards: ReadonlyArray<DemoSpan>): (t: number) => number {
  const segments = fastForwards
    .map((s) => ({ start: Math.max(s.start, keep.start), end: Math.min(s.end, keep.end) }))
    .filter((s) => s.end > s.start)
    .sort((a, b) => a.start - b.start);

  return (t: number): number => {
    const clamped = Math.min(Math.max(t, keep.start), keep.end);
    let output = clamped - keep.start;
    // Each segment before (or around) this time gives back the time it saves.
    for (const s of segments) {
      if (clamped <= s.start) break;
      const length = s.end - s.start;
      const covered = Math.min(clamped, s.end) - s.start;
      output -= covered - (covered * FAST_FORWARD_OUTPUT_MS) / length;
    }
    return output;
  };
}

/**
 * Writes an ffmpeg concat-demuxer list that shows each frame for as long as it was on screen, in video
 * time. The frame on screen when the video starts is shown from 0; frames painted after the end are
 * dropped. The last frame is listed twice because the concat demuxer ignores the final duration.
 *
 * @param frames - The frames, in painting order.
 * @param keep - The recording span the video shows.
 * @param map - Recording time to video time, from {@link createTimeMap}.
 * @returns The list file's contents.
 * @throws Error when no frame was painted at or before `keep.end`.
 */
export function buildConcatList(frames: ReadonlyArray<DemoFrame>, keep: DemoSpan, map: (t: number) => number): string {
  // The last frame painted before the start is what is on screen at 0 (`findLastIndex` is ES2023).
  let firstIndex = 0;
  frames.forEach((f, i) => {
    if (f.t <= keep.start) firstIndex = i;
  });
  const shown = frames.slice(firstIndex).filter((f) => f.t <= keep.end);
  if (shown.length === 0) throw new Error("No frames were captured inside the kept part of the recording.");

  const lines: Array<string> = ["ffconcat version 1.0"];
  shown.forEach((frame, i) => {
    const from = map(frame.t);
    const to = map(shown[i + 1]?.t ?? keep.end);
    lines.push(`file '${frame.file.replace(/'/g, "'\\''")}'`, `duration ${((to - from) / 1000).toFixed(3)}`);
  });
  lines.push(`file '${shown.at(-1)!.file.replace(/'/g, "'\\''")}'`);
  return `${lines.join("\n")}\n`;
}

/**
 * Formats video time as a WebVTT timestamp (`HH:MM:SS.mmm`).
 *
 * @param ms - Video time in milliseconds.
 * @returns The timestamp.
 */
export function vttTimestamp(ms: number): string {
  const total = Math.max(0, Math.round(ms));
  const h = Math.floor(total / 3_600_000);
  const m = Math.floor((total % 3_600_000) / 60_000);
  const s = Math.floor((total % 60_000) / 1000);
  const pad = (n: number, w = 2): string => String(n).padStart(w, "0");
  return `${pad(h)}:${pad(m)}:${pad(s)}.${pad(total % 1000, 3)}`;
}

/**
 * Writes the captions as a WebVTT file in video time. Captions that fall entirely outside the kept part
 * of the recording are left out.
 *
 * @param captions - The captions, in recording time.
 * @param map - Recording time to video time, from {@link createTimeMap}.
 * @returns The WebVTT file's contents.
 */
export function toWebVtt(captions: ReadonlyArray<DemoCaption>, map: (t: number) => number): string {
  const cues = captions
    .map((c) => ({ start: map(c.start), end: map(c.end), text: c.text }))
    .filter((c) => c.end > c.start)
    .map((c, i) => `${i + 1}\n${vttTimestamp(c.start)} --> ${vttTimestamp(c.end)}\n${c.text}`);
  return `WEBVTT\n\n${cues.join("\n\n")}${cues.length ? "\n" : ""}`;
}

/**
 * Builds an ffmpeg `enable` expression that is true while any of the spans is playing, in seconds of
 * video time.
 *
 * @param spans - Spans in recording time.
 * @param map - Recording time to video time.
 * @returns The expression, or `"0"` when there are no spans.
 */
export function enableExpression(spans: ReadonlyArray<DemoSpan>, map: (t: number) => number): string {
  const parts = spans
    .map((s) => ({ start: map(s.start) / 1000, end: map(s.end) / 1000 }))
    .filter((s) => s.end > s.start)
    .map((s) => `between(t,${s.start.toFixed(3)},${s.end.toFixed(3)})`);
  return parts.length ? parts.join("+") : "0";
}
