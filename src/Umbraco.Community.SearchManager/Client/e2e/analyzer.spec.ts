import { expect, test } from "./support/test.js";
import { analyzerColumn, openAnalyzer } from "./support/search-manager.js";

/** The text the analyzer view is verified with: an accent, a number with a colon and a stop word. */
const TEXT = "Café menu 2026: the opening hours";

test("shows the stop word struck out in ExternalIndex's StopFilter step", async ({ page }) => {
  await openAnalyzer(page, { text: TEXT });

  const stop = analyzerColumn(page, "ExternalIndex").locator('li[data-stage="StopFilter"]');
  await expect(stop.locator('[data-state="Removed"]')).toHaveText(/^the/);
  // InternalIndex folds the accent and keeps every word.
  const internal = analyzerColumn(page, "InternalIndex").locator("li[data-final]");
  await expect(internal).toContainText("cafe");
  await expect(internal.locator('[data-state="Removed"]')).toHaveCount(0);
});

test("judges an unaccented search: no match where the accent is kept, a match where it is folded", async ({ page }) => {
  await openAnalyzer(page);
  await page.locator("search-manager-analyzer uui-input#text input").fill(TEXT);
  await page.locator("search-manager-analyzer uui-input#term input").fill("cafe");

  const external = analyzerColumn(page, "ExternalIndex");
  const internal = analyzerColumn(page, "InternalIndex");
  await expect(external.getByRole("status")).toHaveAttribute("data-verdict", "no-match");
  await expect(internal.getByRole("status")).toHaveAttribute("data-verdict", "match");
  await expect(internal.locator('li[data-final] [data-match="Exact"]')).toContainText("cafe");

  // The text and the search are in the URL, so a reload shows the same answer.
  await expect(page).toHaveURL(/term=cafe/);
  await page.reload();
  await expect(analyzerColumn(page, "InternalIndex").getByRole("status")).toHaveAttribute("data-verdict", "match", {
    timeout: 60_000,
  });
});

test("finds a number in both indexes", async ({ page }) => {
  await openAnalyzer(page, { text: TEXT, term: "2026" });

  await expect(analyzerColumn(page, "ExternalIndex").getByRole("status")).toHaveAttribute("data-verdict", "match");
  await expect(analyzerColumn(page, "InternalIndex").getByRole("status")).toHaveAttribute("data-verdict", "match");
});
