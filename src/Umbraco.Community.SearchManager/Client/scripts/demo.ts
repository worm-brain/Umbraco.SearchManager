import { spawnSync } from "node:child_process";
import { existsSync, readdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { renderVideo } from "./demo-render.js";

/**
 * `bun run docs:demo [-- <video>...] [--render-only] [--keep-frames]`: records the demo videos with
 * `playwright.demo.config.ts`, then renders them into `<repo>/artifacts/demo/`. With no names it records
 * every spec in `e2e/demo/`; `--render-only` re-renders recordings kept with `--keep-frames`.
 * Identical in every package repo (package standard, demo videos).
 */

const client = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** Walks up from the client folder to the repository root (the folder holding `.git`). */
function findRepoRoot(from: string): string {
  for (let dir = from; ; dir = dirname(dir)) {
    if (existsSync(join(dir, ".git"))) return dir;
    if (dirname(dir) === dir) throw new Error(`No repository root above ${from}.`);
  }
}

const args = process.argv.slice(2);
const renderOnly = args.includes("--render-only");
const keepFrames = args.includes("--keep-frames");
const names = args.filter((a) => !a.startsWith("--"));
const repoRoot = findRepoRoot(client);
const outDir = join(repoRoot, "artifacts", "demo");

if (!renderOnly) {
  // Playwright runs under Node: under Bun it hangs launching Chromium on Windows.
  const files = names.map((n) => `${n}.demo.ts`);
  const run = spawnSync(
    "node",
    [join(client, "node_modules/@playwright/test/cli.js"), "test", "-c", "playwright.demo.config.ts", ...files],
    {
      cwd: client,
      stdio: "inherit",
    },
  );
  if (run.status !== 0) process.exit(run.status ?? 1);
}

const videos = names.length
  ? names
  : existsSync(outDir)
    ? readdirSync(outDir).filter((d) => existsSync(join(outDir, d, "timeline.json")))
    : [];
for (const video of videos) {
  console.log(`Rendering ${video}...`);
  for (const file of renderVideo(join(outDir, video), { repoRoot, keepFrames })) console.log(`  ${file}`);
}
