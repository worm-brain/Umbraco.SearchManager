import { expect, test } from "./support/test.js";
import { findNode, openExplore, presenceRow, waitForSeededIndex } from "./support/search-manager.js";

/** Umbraco Search's draft content index (Examine provider). */
const CONTENT = "Umb_Content";

test("shows one Umbraco Search document per culture of a variant page", async ({ page }) => {
  // On a fresh site Umbraco Search may still be indexing the seeded content.
  await openExplore(page, CONTENT);
  await page.locator("uui-select#culture select").selectOption("en-US");
  await waitForSeededIndex(page, 10);

  // Published in en-US and da-DK (SampleContentSeeder: i = 0).
  await findNode(page, "Guide to Umbraco 1");

  const row = presenceRow(page, CONTENT);
  await expect(row).toContainText("Present");
  await expect(row).toContainText("en-US");
  await expect(row).toContainText("da-DK");
  await expect(row.locator("uui-table-cell.number")).toHaveText("2");
});
