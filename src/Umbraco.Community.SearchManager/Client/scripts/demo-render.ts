import { spawnSync } from "node:child_process";
import { copyFileSync, existsSync, mkdirSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { buildConcatList, createTimeMap, enableExpression, toWebVtt, type DemoTimeline } from "./demo-timeline.js";

/**
 * Renders a recorded demo (`<outDir>/<video>/timeline.json` and its frames) into the published files.
 * Identical in every package repo (package standard, demo videos). Needs `ffmpeg` on the PATH.
 *
 * Writes next to the video's folder, in `<outDir>`:
 * - `<video>.mp4`: the master, H.264 at the recorded size, for YouTube and release posts;
 * - `<video>.vtt`: the captions as WebVTT, for YouTube's subtitle track;
 * - `<video>.readme.mp4`: a 1280-wide cut under {@link README_LIMIT_BYTES}, for the GitHub README;
 * - `<video>.poster.png`: the poster frame, also copied to `<repo>/docs/images/demo-<video>.png`.
 */

/** GitHub plays a README video attachment only up to 10 MB. */
const README_LIMIT_BYTES = 10 * 1024 * 1024;

/** Options for {@link renderVideo}. */
export interface RenderOptions {
  /** The repository root; the poster is copied into its `docs/images/`. */
  repoRoot: string;
  /** Keep the captured frames after rendering, to re-render without recording again. */
  keepFrames?: boolean;
}

/**
 * Runs ffmpeg and fails loudly with its output.
 *
 * @param args - The arguments after `ffmpeg`.
 * @param cwd - The working folder; concat lists resolve frame paths against it.
 * @throws Error when ffmpeg cannot be started or exits non-zero.
 */
function ffmpeg(args: Array<string>, cwd: string): void {
  const run = spawnSync("ffmpeg", ["-hide_banner", "-loglevel", "error", "-y", ...args], { cwd, encoding: "utf8" });
  if (run.error) throw new Error(`Could not start ffmpeg (is it on the PATH?): ${run.error.message}`);
  if (run.status !== 0) throw new Error(`ffmpeg failed (${run.status}):\n${run.stderr}`);
}

/**
 * Renders one recorded video.
 *
 * @param dir - The video's folder, holding `timeline.json`, `frames/` and `ff-badge.png`.
 * @param options - The repository root and whether to keep the frames.
 * @returns The paths of the files written.
 * @throws Error when the folder has no timeline or ffmpeg fails.
 */
export function renderVideo(dir: string, options: RenderOptions): Array<string> {
  const timelinePath = join(dir, "timeline.json");
  if (!existsSync(timelinePath)) throw new Error(`No recording in ${dir}: run the demo spec first.`);
  const timeline = JSON.parse(readFileSync(timelinePath, "utf8")) as DemoTimeline;
  const out = dirname(dir);
  const name = timeline.video;
  const { width, height } = timeline.size;

  // 1. Remap every frame through the time map: the load-in is cut and long waits are squeezed.
  const map = createTimeMap(timeline.keep, timeline.fastForwards);
  writeFileSync(join(dir, "concat.txt"), buildConcatList(timeline.frames, timeline.keep, map));
  const seconds = (map(timeline.keep.end) / 1000).toFixed(3);

  // 2. The master. The badge is overlaid only while a fast-forward plays.
  const master = join(out, `${name}.mp4`);
  // Screencast JPEGs are full range; convert to the limited range players expect.
  const base = `fps=30,scale=${width}:${height}:flags=lanczos:out_range=tv,setsar=1`;
  const badge = join(dir, "ff-badge.png");
  const squeezed = timeline.fastForwards.length > 0 && existsSync(badge);
  const video = squeezed
    ? [
        "-i",
        badge,
        "-filter_complex",
        `[0:v]${base}[b];[b][1:v]overlay=x=main_w-overlay_w-32:y=32:enable='${enableExpression(timeline.fastForwards, map)}',format=yuv420p[v]`,
        "-map",
        "[v]",
      ]
    : ["-vf", `${base},format=yuv420p`];
  ffmpeg(
    [
      "-f",
      "concat",
      "-safe",
      "0",
      "-i",
      "concat.txt",
      ...video,
      "-t",
      seconds,
      "-c:v",
      "libx264",
      "-preset",
      "slow",
      "-crf",
      "16",
      "-movflags",
      "+faststart",
      master,
    ],
    dir,
  );

  // 3. Captions in video time.
  const vtt = join(out, `${name}.vtt`);
  writeFileSync(vtt, toWebVtt(timeline.captions, map));

  // 4. The README cut: raise the CRF until it fits GitHub's limit.
  const readme = join(out, `${name}.readme.mp4`);
  for (let crf = 24; ; crf += 3) {
    ffmpeg(
      [
        "-i",
        master,
        "-vf",
        "scale=1280:-2:flags=lanczos",
        "-c:v",
        "libx264",
        "-preset",
        "slow",
        "-crf",
        String(crf),
        "-movflags",
        "+faststart",
        readme,
      ],
      out,
    );
    if (statSync(readme).size < README_LIMIT_BYTES) break;
    if (crf >= 36) throw new Error(`${readme} is still over 10 MB at CRF ${crf}; shorten the video.`);
  }

  // 5. The poster: the marked moment, or the middle of the video.
  const poster = join(out, `${name}.poster.png`);
  const at = timeline.poster === undefined ? Number(seconds) / 2 : map(timeline.poster) / 1000;
  ffmpeg(["-ss", at.toFixed(3), "-i", master, "-frames:v", "1", poster], out);
  const images = join(options.repoRoot, "docs", "images");
  mkdirSync(images, { recursive: true });
  const docsPoster = join(images, `demo-${name}.png`);
  copyFileSync(poster, docsPoster);

  if (!options.keepFrames) rmSync(join(dir, "frames"), { recursive: true, force: true });
  return [master, vtt, readme, poster, docsPoster];
}
