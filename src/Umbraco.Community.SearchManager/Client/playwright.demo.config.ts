import { defineConfig } from "@playwright/test";
import { sites } from "./e2e/support/sites.js";
import type { E2eOptions } from "./e2e/support/test.js";

/**
 * Records the demo videos (`bun run docs:demo`, which also renders them into `artifacts/demo/`), from the
 * Umbraco 17 sample site with Umbraco Search, like the documentation screenshots. It reuses a site already
 * running on its own launch port (44382, `dotnet run` in `samples/SearchManager.SearchSite17`), or starts
 * one there.
 *
 * The viewport is 1920x1080 at device scale factor 1 (Chromium screencasts CSS pixels); the overlay zooms the
 * page by 4/3, so the backoffice lays out at 1440x810 and reads larger than it would at 1920x1080. Signing in
 * ends any other backoffice session of the admin.
 */
const port = Number(process.env.DOCS_SITE_PORT ?? 44382);
const search17 = sites.find((site) => site.name === "search17");
if (!search17) throw new Error("E2E_SITES must include 17 for the demo videos.");
const site = { ...search17, port, baseURL: `https://localhost:${port}` };

export default defineConfig<E2eOptions>({
  testDir: "e2e/demo",
  testMatch: /\.demo\.ts$/,
  outputDir: "e2e/test-results",
  // One video at a time: they share the site and some rebuild indexes.
  workers: 1,
  timeout: 600_000,
  expect: { timeout: 30_000 },
  reporter: [["list"]],
  use: {
    ignoreHTTPSErrors: true,
    // Chromium screencasts CSS pixels, so record at full size; the overlay zooms the page by 4/3.
    viewport: { width: 1920, height: 1080 },
    deviceScaleFactor: 1,
    locale: "en-GB",
    colorScheme: "light",
    site,
    baseURL: site.baseURL,
  },
  webServer: {
    command: `dotnet run --no-build --no-launch-profile --urls https://localhost:${port}`,
    cwd: site.projectDir,
    env: { ASPNETCORE_ENVIRONMENT: "Development" },
    url: `${site.baseURL}/umbraco`,
    ignoreHTTPSErrors: true,
    timeout: 600_000,
    reuseExistingServer: true,
    stdout: "ignore",
    stderr: "pipe",
  },
});
