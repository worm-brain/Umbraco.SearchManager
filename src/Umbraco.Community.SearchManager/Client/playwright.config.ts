import { defineConfig, type PlaywrightTestProject } from "@playwright/test";
import { sites } from "./e2e/support/sites.js";
import type { E2eOptions } from "./e2e/support/test.js";

const ci = !!process.env.CI;

/**
 * The e2e suite: runs the specs against each selected sample site (`E2E_SITES`), starting the
 * sites itself. `bun run e2e` builds the client and the sites first (`e2e/prepare.ts`).
 * Specs named `*.umbraco-search.spec.ts` run only on the Umbraco Search sites.
 */
export default defineConfig<E2eOptions>({
  testDir: "e2e",
  outputDir: "e2e/test-results",
  // Specs share a site; the rebuild spec only rebuilds indexes no other spec searches.
  fullyParallel: true,
  workers: ci ? 2 : 4,
  forbidOnly: ci,
  retries: ci ? 1 : 0,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  reporter: ci
    ? [["github"], ["html", { open: "never", outputFolder: "e2e/playwright-report" }]]
    : [["list"], ["html", { open: "never", outputFolder: "e2e/playwright-report" }]],
  use: {
    // The sites use the ASP.NET Core development certificate, which CI does not trust.
    ignoreHTTPSErrors: true,
    viewport: { width: 1440, height: 900 },
    locale: "en-GB",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  // One project per site; each test gets a logged-in page from e2e/support/test.ts.
  projects: sites.map((site): PlaywrightTestProject<E2eOptions> => ({
    name: site.name,
    testMatch: /\.spec\.ts$/,
    testIgnore: site.umbracoSearch ? [] : [/\.umbraco-search\.spec\.ts$/],
    use: { site, baseURL: site.baseURL },
  })),
  webServer: sites.map((site) => ({
    // --no-build: e2e/prepare.ts has built the client and the site, in that order.
    command: `dotnet run --no-build --no-launch-profile -p:UmbracoMajor=${site.major} --urls https://localhost:${site.port}`,
    cwd: site.projectDir,
    env: {
      ASPNETCORE_ENVIRONMENT: "Development",
      // Every test logs in as the same admin (e2e/support/auth.ts). With concurrent logins off,
      // each login revokes every other session of that user, so parallel tests would sign each
      // other out.
      Umbraco__CMS__Security__AllowConcurrentLogins: "true",
    },
    // Answers once Umbraco has booted; on a fresh database that includes the unattended install
    // and the sample content seed.
    url: `${site.baseURL}/umbraco`,
    ignoreHTTPSErrors: true,
    timeout: 600_000,
    // Locally, a site already running on the e2e port is used as is, so start it with the env
    // above (at least AllowConcurrentLogins), or parallel tests sign each other out.
    reuseExistingServer: !ci,
    stdout: "ignore",
    stderr: "pipe",
  })),
});
