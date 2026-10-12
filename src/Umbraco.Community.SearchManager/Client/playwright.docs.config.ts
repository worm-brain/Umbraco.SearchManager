import { defineConfig } from "@playwright/test";
import { sites } from "./e2e/support/sites.js";
import type { E2eOptions } from "./e2e/support/test.js";

/**
 * Takes the documentation screenshots in `docs/images/` (`bun run docs:screenshots`), from the
 * Umbraco 17 sample site with Umbraco Search. It reuses a site already running on its own launch
 * port (44382, `dotnet run` in `samples/SearchManager.SearchSite17`), or starts one there.
 *
 * Signing in starts a new backoffice session for the admin, which ends any other session of that
 * user on the site unless concurrent logins are allowed.
 */
const port = Number(process.env.DOCS_SITE_PORT ?? 44382);
const search17 = sites.find((site) => site.name === "search17");
if (!search17) throw new Error("E2E_SITES must include 17 for the documentation screenshots.");
const site = { ...search17, port, baseURL: `https://localhost:${port}` };

export default defineConfig<E2eOptions>({
  testDir: "e2e/docs",
  testMatch: /\.docs\.ts$/,
  outputDir: "e2e/test-results",
  // One worker, one session: the shots run in order and some build on the previous one's state.
  workers: 1,
  timeout: 300_000,
  expect: { timeout: 30_000 },
  reporter: [["list"]],
  use: {
    ignoreHTTPSErrors: true,
    // The size the screenshots are published at; scale 2 keeps text sharp on high-density screens.
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 2,
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
