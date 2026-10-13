import { expect, test } from "./support/test.js";
import { openIndexes } from "./support/search-manager.js";

/**
 * The index this spec rebuilds through Umbraco's own Management API. No other spec rebuilds or
 * searches it, so the rebuild never races another test.
 */
const INDEX = "MembersIndex";

test("records a core Examine rebuild started outside Search Manager in the history", async ({ page }) => {
  test.setTimeout(180_000);
  await openIndexes(page, INDEX);

  // What the core Examine dashboard does. The backoffice keeps its token in an httpOnly cookie and
  // sends this placeholder, which the server swaps for the cookie's token.
  const status = await page.evaluate(
    async (index) =>
      (
        await fetch(`/umbraco/management/api/v1/indexer/${index}/rebuild`, {
          method: "POST",
          headers: { authorization: "Bearer [redacted]" },
        })
      ).status,
    INDEX,
  );
  expect(status).toBe(200);

  // Umbraco rebuilds in the background; the entry appears once it has finished.
  await expect(async () => {
    await page.reload();
    await page.getByRole("button", { name: `Details and history for ${INDEX}` }).click({ timeout: 30_000 });
    const history = page.locator("uui-box").filter({ hasText: "Rebuild history" });
    await expect(history.getByText("Started outside Search Manager").first()).toBeVisible({ timeout: 5_000 });
  }).toPass({ timeout: 120_000, intervals: [2_000, 5_000] });
});
