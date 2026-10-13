import { expect, test } from "./support/test.js";
import { emptyUmbracoSearchIndexes, settled, type ApiIndex, type ApiRun } from "./support/umbraco-search-setup.js";

/**
 * Runs before the specs of an Umbraco Search sample site (a Playwright setup project, see
 * playwright.config.ts): rebuilds every Umbraco Search index that holds no documents.
 *
 * e2e/prepare.ts seeds the sample content with Umbraco Search left out, so its indexes miss that
 * content. Umbraco Search's own boot rebuild only builds an index that does not exist yet, so it
 * does not fill them. Rebuilding through Search Manager's API does.
 */
test("rebuilds the empty Umbraco Search indexes", async ({ page }) => {
  test.setTimeout(300_000);
  await page.goto("/umbraco/section/settings");

  // The backoffice keeps its token in an httpOnly cookie and sends this placeholder, which the
  // server swaps for the cookie's token.
  const api = (path: string, init?: RequestInit) =>
    page.evaluate(
      async ({ path, init }) => {
        const response = await fetch(`/umbraco/search-manager/api/v1${path}`, {
          ...init,
          headers: { authorization: "Bearer [redacted]", "content-type": "application/json" },
        });
        return { status: response.status, body: response.status === 200 ? await response.json() : null };
      },
      { path, init },
    );

  const indexes = await api("/indexes");
  expect(indexes.status).toBe(200);
  const names = emptyUmbracoSearchIndexes(indexes.body as ReadonlyArray<ApiIndex>);
  if (names.length === 0) return;

  const queued = await api("/rebuilds", {
    method: "POST",
    body: JSON.stringify({ indexNames: names, mode: "AllAtOnce" }),
  });
  expect(queued.status).toBe(200);

  await expect(async () => {
    const runs = await api("/rebuilds");
    expect(settled(runs.body as ReadonlyArray<ApiRun>, names)).toBe(true);
  }).toPass({ timeout: 240_000, intervals: [2_000, 5_000] });
});
