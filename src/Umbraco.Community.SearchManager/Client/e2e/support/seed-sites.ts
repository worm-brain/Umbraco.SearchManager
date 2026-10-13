import { spawn, spawnSync, type ChildProcess } from "node:child_process";
import { existsSync, rmSync } from "node:fs";
import { join } from "node:path";
import { createInterface } from "node:readline";
import { readSeedSignal } from "./seed-log.js";
import type { E2eSite } from "./sites.js";

/** Attempts per site before giving up. */
const ATTEMPTS = 3;
/** How long a first boot may take to install and seed. */
const SEED_TIMEOUT_MS = 8 * 60_000;
/**
 * How long the site keeps running after seeding, so the indexing it set off finishes and a stalled
 * cache instruction sync (reported about a minute after it stalls) shows up here rather than in a
 * test.
 */
const SETTLE_MS = 75_000;

/**
 * Gives each selected sample site its first boot before Playwright starts it: the unattended
 * install, the sample content seed and the indexing that follows. A site whose database already
 * exists is left alone.
 *
 * A fresh Umbraco Search site on SQLite can deadlock during that first boot (Umbraco issue 23940:
 * the cache instruction sync holds a transaction while it waits on async cache refresher handlers,
 * which Umbraco Search registers). It is rare locally and common on slow CI runners, and it never
 * clears. So a first boot that stalls, times out or exits early is retried on a fresh database,
 * and the tests then start a site that has finished booting once.
 *
 * Sites are seeded one at a time, so they do not compete for the machine while they seed.
 *
 * @param sites - The sites this run covers.
 * @throws When a site cannot complete a first boot in {@link ATTEMPTS} tries.
 */
export async function seedSites(sites: ReadonlyArray<E2eSite>): Promise<void> {
  for (const site of sites) {
    const data = join(site.projectDir, "umbraco", "Data");
    if (existsSync(join(data, "Umbraco.sqlite.db"))) continue;

    for (let attempt = 1; ; attempt++) {
      console.log(`> first boot of ${site.name} (attempt ${attempt} of ${ATTEMPTS})`);
      const outcome = await firstBoot(site);
      if (outcome === "ok") break;

      console.log(`  ${site.name}: ${outcome}; starting again on a fresh database`);
      rmSync(data, { recursive: true, force: true });
      if (attempt === ATTEMPTS) throw new Error(`${site.name} did not complete a first boot: ${outcome}`);
    }
  }
}

/**
 * Runs one first boot of a site and stops it again.
 *
 * @param site - The site, with no database yet.
 * @returns "ok", or why the boot failed.
 */
async function firstBoot(site: E2eSite): Promise<string> {
  const child = spawn(
    "dotnet",
    ["run", "--no-build", "--no-launch-profile", `-p:UmbracoMajor=${site.major}`, "--urls", site.baseURL],
    {
      cwd: site.projectDir,
      // The same environment Playwright starts the site with (playwright.config.ts).
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: "Development",
        Umbraco__CMS__Security__AllowConcurrentLogins: "true",
      },
      stdio: ["ignore", "pipe", "pipe"],
      // Its own process group on Linux and macOS, so stopping it also stops the site dotnet run starts.
      detached: process.platform !== "win32",
    },
  );

  try {
    return await new Promise<string>((resolve) => {
      let settle: NodeJS.Timeout | undefined;
      const timeout = setTimeout(() => resolve("timed out before seeding finished"), SEED_TIMEOUT_MS);
      const finish = (outcome: string) => {
        clearTimeout(timeout);
        clearTimeout(settle);
        resolve(outcome);
      };

      createInterface({ input: child.stdout! }).on("line", (line) => {
        const signal = readSeedSignal(line);
        if (signal === "hung") finish("the cache instruction sync stalled");
        if (signal === "seeded" && !settle) settle = setTimeout(() => finish("ok"), SETTLE_MS);
      });
      child.on("exit", (code) => finish(`exited with code ${code}`));
    });
  } finally {
    await stop(child);
  }
}

/**
 * Stops a site started with `dotnet run`, including the site process it launched.
 *
 * @param child - The `dotnet run` process.
 */
async function stop(child: ChildProcess): Promise<void> {
  if (child.exitCode !== null || child.pid === undefined) return;
  const exited = new Promise<void>((resolve) => child.once("exit", () => resolve()));
  if (process.platform === "win32") {
    spawnSync("taskkill", ["/pid", String(child.pid), "/T", "/F"], { stdio: "ignore" });
  } else {
    // SQLite's write-ahead log recovers from a hard stop, so there is no need to wait for a clean one.
    process.kill(-child.pid, "SIGKILL");
  }
  await exited;
}
