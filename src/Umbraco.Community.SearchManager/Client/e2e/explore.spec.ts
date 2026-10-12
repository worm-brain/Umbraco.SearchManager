import { expect, test } from "./support/test.js";
import { openExplore, resultsCount, searchBox, waitForSeededIndex } from "./support/search-manager.js";

test.beforeEach(async ({ page }) => {
  await openExplore(page);
  await waitForSeededIndex(page);
});

test("turns typed words and field filters into chips that narrow the results", async ({ page }) => {
  const before = await resultsCount(page).textContent();

  await searchBox(page).fill("umbraco type:blogPost");
  await searchBox(page).press("Enter");

  const chips = page.locator("search-manager-filter-chip");
  await expect(chips).toHaveCount(2);
  await expect(chips.nth(1)).toContainText("blogPost");
  await expect(resultsCount(page)).not.toHaveText(before ?? "");
  // The view is in the URL, so a reload restores the chips.
  await page.reload();
  await expect(page.locator("search-manager-filter-chip")).toHaveCount(2);
});

test("opens a result's stored fields in the drawer", async ({ page }) => {
  await page.locator("search-manager-results button.open").first().click();

  const drawer = page.locator("search-manager-document-drawer");
  await expect(drawer.getByText("__NodeId").first()).toBeVisible();
  // Beside the results, not wrapped below them: at 1440 px the three columns do not fit on a line.
  await expect(drawer).toBeInViewport();
});

test("reports a Lucene syntax error in native mode", async ({ page }) => {
  await page.getByRole("button", { name: "Lucene", exact: true }).click();
  const native = page.locator("search-manager-native-box uui-input input");

  await native.fill("+nodeName:(mug");

  await expect(page.locator("search-manager-native-box").getByText("Cannot parse this query")).toBeVisible();
});
