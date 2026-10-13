import { expect, test } from "./support/test.js";
import {
  fieldRow,
  firstNumber,
  openExplore,
  openFieldStatistics,
  resultsCount,
  waitForSeededIndex,
} from "./support/search-manager.js";

test.beforeEach(async ({ page }) => {
  // The statistics count whatever is indexed; wait for the seed, as the Explore specs do.
  await openExplore(page);
  await waitForSeededIndex(page);
  await openFieldStatistics(page);
});

test("links a field's missing count to the documents Explore finds without it", async ({ page }) => {
  // bodyText is invariant on blog posts and products only, so the other pages miss it.
  const missing = fieldRow(page, "bodyText").locator("uui-button.missing");
  await expect(missing).toBeVisible();
  const count = firstNumber(await missing.textContent());
  expect(count).toBeGreaterThan(0);

  await missing.click();

  await expect(page).toHaveURL(/search-manager\/view\/explore\?index=ExternalIndex/);
  await expect(page.locator("search-manager-filter-chip")).toHaveCount(1);
  await expect(page.locator("search-manager-filter-chip")).toContainText("missing");
  await expect.poll(async () => firstNumber(await resultsCount(page).textContent())).toBe(count);
});

test("opens Explore with a chip from a keyword field's top term", async ({ page }) => {
  // __NodeTypeAlias is a system field: it shows once the checkbox is on.
  await expect(fieldRow(page, "__NodeTypeAlias")).toHaveCount(0);
  await page.locator("search-manager-field-statistics uui-checkbox").click();
  const term = fieldRow(page, "__NodeTypeAlias").locator(".terms uui-button").first();
  await expect(term).toBeVisible();
  const value = ((await term.locator(".term").textContent()) ?? "").trim();
  // The checkbox is in the URL, so a reload keeps the system rows.
  await page.reload();
  await expect(term).toBeVisible({ timeout: 60_000 });

  await term.click();

  await expect(page).toHaveURL(/search-manager\/view\/explore\?index=ExternalIndex/);
  const chips = page.locator("search-manager-filter-chip");
  await expect(chips).toHaveCount(1);
  await expect(chips.first()).toContainText(value);
});
