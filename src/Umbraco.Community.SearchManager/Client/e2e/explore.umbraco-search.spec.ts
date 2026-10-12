import { expect, test } from "./support/test.js";
import { openExplore, openIndexes, resultsCount, searchBox, waitForSeededIndex } from "./support/search-manager.js";

/** Umbraco Search's published content index (Examine provider). */
const PUBLISHED = "Umb_PublishedContent";

test("lists Umbraco Search indexes beside the core Examine ones, with the engine filter", async ({ page }) => {
  await openIndexes(page, PUBLISHED);

  await expect(page.getByRole("group", { name: "Engine" })).toBeVisible();
  await page.getByRole("button", { name: /^Umbraco Search \(\d+\)$/ }).click();
  await expect(page.getByText("ExternalIndex", { exact: true })).toHaveCount(0);
  await expect(page.getByText(PUBLISHED, { exact: true }).first()).toBeVisible();
});

test("searches an Umbraco Search index with chips", async ({ page }) => {
  await openExplore(page, PUBLISHED);
  await page.locator("uui-select#culture select").selectOption("en-US");
  await waitForSeededIndex(page, 10);
  const before = await resultsCount(page).textContent();

  await searchBox(page).fill("umbraco");
  await searchBox(page).press("Enter");

  await expect(page.locator("search-manager-filter-chip")).toHaveCount(1);
  await expect(resultsCount(page)).not.toHaveText(before ?? "");
});

test("adds a Search Manager box to Umbraco Search's index page", async ({ page }) => {
  await page.goto(`/umbraco/section/settings/workspace/search-index/edit/${PUBLISHED}`);

  const box = page.locator("uui-box").filter({ hasText: "Search Manager" });
  await expect(box).toBeVisible({ timeout: 30_000 });
  await box.locator(`uui-button[label="Explore"]`).click();

  await expect(page).toHaveURL(/search-manager\/view\/explore\?index=Umb_PublishedContent/);
});
