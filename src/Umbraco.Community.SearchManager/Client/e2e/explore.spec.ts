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

test("explains the top result of a word search", async ({ page }) => {
  await searchBox(page).fill("umbraco");
  await searchBox(page).press("Enter");
  await expect(page.locator("search-manager-filter-chip")).toHaveCount(1);

  await page.locator("search-manager-results button.open").first().click();
  const drawer = page.locator("search-manager-document-drawer");
  await drawer.getByRole("button", { name: "Explain score" }).click();

  const tree = page.getByRole("list", { name: /^How the score of .+ is made up$/ });
  await expect(tree.getByText(/contains "umbraco"/).first()).toBeVisible();
  await expect(tree.getByText(/idf \d/).first()).toBeVisible();
});

test("compares two results field by field with differences only", async ({ page }) => {
  await searchBox(page).fill("guide umbraco");
  await searchBox(page).press("Enter");
  await expect(page.locator("search-manager-filter-chip")).toHaveCount(1);

  const boxes = page.locator('search-manager-results uui-checkbox[aria-label^="Compare the score of "]');
  await boxes.nth(0).click();
  await boxes.nth(1).click();
  // Two are the most a comparison takes.
  await expect(boxes.nth(2)).toHaveAttribute("disabled");
  await page.getByRole("button", { name: "Compare scores" }).click();

  const table = page.locator('search-manager-score-compare uui-table[aria-label="Score comparison"]');
  const rows = table.locator("uui-table-row");
  await expect(rows.first()).toBeVisible();
  const all = await rows.count();
  await page.locator('search-manager-score-compare uui-toggle[label="Differences only"]').click();
  // The query normalisation is the same for both documents, so at least that row goes.
  await expect.poll(() => rows.count()).toBeLessThan(all);
});

test("reads a single hit as 1 document", async ({ page }) => {
  await searchBox(page).fill("type:home");
  await searchBox(page).press("Enter");

  await expect(resultsCount(page)).toHaveText("1 document, newest first");
});

test("keeps a field's other values in the fields panel after picking one", async ({ page }) => {
  await searchBox(page).fill("type:product");
  await searchBox(page).press("Enter");
  await expect(page.locator("search-manager-filter-chip")).toHaveCount(1);

  // Counted without the field's own chip, so the other document types stay listed, as written.
  const values = page.locator("search-manager-fields-panel section.facet .value button .text");
  await expect(values.filter({ hasText: /^blogPost$/ })).toBeVisible();
});
