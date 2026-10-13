import { expect, test } from "./support/test.js";
import { fieldRow, openExplore, openFieldStatistics, waitForSeededIndex } from "./support/search-manager.js";

/** Umbraco Search's draft content index (Examine provider). */
const CONTENT = "Umb_Content";

test("shows the field statistics of an Umbraco Search index by logical field name", async ({ page }) => {
  await openExplore(page, CONTENT);
  await page.locator("uui-select#culture select").selectOption("en-US");
  await waitForSeededIndex(page, 10);

  await openFieldStatistics(page, CONTENT);

  const statistics = page.locator("search-manager-field-statistics");
  await expect(statistics.getByText("one document per culture")).toBeVisible();
  await expect(fieldRow(page, "bodyText")).toBeVisible();
  await expect(fieldRow(page, "Umb_Name")).toBeVisible();
  // The provider's own fields, such as Sys_Culture, show with the checkbox on.
  await statistics.locator("uui-checkbox").click();
  await expect(fieldRow(page, "Sys_Culture")).toBeVisible();
});

test("links the Search Manager box on Umbraco Search's index page to field statistics", async ({ page }) => {
  // On a fresh site Umbraco Search may still be indexing the seeded content.
  await openExplore(page, CONTENT);
  await page.locator("uui-select#culture select").selectOption("en-US");
  await waitForSeededIndex(page, 10);

  await page.goto(`/umbraco/section/settings/workspace/search-index/edit/${CONTENT}`);

  const box = page.locator("uui-box").filter({ hasText: "Search Manager" });
  await expect(box).toBeVisible({ timeout: 30_000 });
  await box.locator(`uui-button[label="Field statistics"]`).click();

  await expect(page).toHaveURL(/search-manager\/view\/diagnose\?index=Umb_Content&tool=fields/);
  await expect(page.locator("search-manager-field-statistics uui-table")).toBeVisible({ timeout: 60_000 });
});
