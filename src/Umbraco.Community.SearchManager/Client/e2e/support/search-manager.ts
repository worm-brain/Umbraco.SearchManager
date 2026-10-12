import { expect, type Locator, type Page } from "@playwright/test";

/** The Search Manager workspace under Settings > Advanced. */
const WORKSPACE = "/umbraco/section/settings/workspace/search-manager";

/**
 * Opens the Indexes view and waits until the table lists `index`.
 *
 * @param page - A logged-in page.
 * @param index - An index the table must list before this returns.
 */
export async function openIndexes(page: Page, index = "ExternalIndex"): Promise<void> {
  await page.goto(`${WORKSPACE}/view/indexes`);
  await expect(selectBox(page, index)).toBeVisible({ timeout: 60_000 });
}

/**
 * Opens the Explore view on an index and waits for the results list.
 *
 * @param page - A logged-in page.
 * @param index - The index (or searcher) to search.
 */
export async function openExplore(page: Page, index = "ExternalIndex"): Promise<void> {
  await page.goto(`${WORKSPACE}/view/explore?index=${encodeURIComponent(index)}`);
  await expect(page.locator("search-manager-results").first()).toBeVisible({ timeout: 60_000 });
}

/**
 * Waits until the open Explore view shows at least `minimum` results for an empty search,
 * reloading while it does not. A fresh sample site seeds its content and indexes it in the
 * background after it starts answering, so the first spec to search may get there first.
 *
 * @param page - A page on the Explore view with no filters.
 * @param minimum - The number of results that means the seed has been indexed.
 */
export async function waitForSeededIndex(page: Page, minimum = 100): Promise<void> {
  await expect(async () => {
    const count = await resultsCount(page).textContent({ timeout: 10_000 });
    const value = Number((count ?? "").replace(/[^0-9]/g, ""));
    if (value < minimum) {
      await page.reload();
      throw new Error(`Only ${value} results so far`);
    }
  }).toPass({ timeout: 180_000, intervals: [2_000, 5_000] });
}

/**
 * The results count line of the Explore view ("N results", or "N documents, newest first" for an
 * empty search).
 *
 * @param page - A page on the Explore view.
 * @returns The locator of the results count.
 */
export function resultsCount(page: Page): Locator {
  return page.locator("search-manager-results").getByRole("status").first();
}

/**
 * The Explore search box's text input.
 *
 * @param page - A page on the Explore view.
 * @returns The locator of the input.
 */
export function searchBox(page: Page): Locator {
  return page.locator("search-manager-search-box uui-input input").first();
}

/**
 * The selection checkbox of one row of the Indexes table.
 *
 * @param page - A page on the Indexes view.
 * @param index - The index name.
 * @returns The locator of the row's checkbox.
 */
export function selectBox(page: Page, index: string): Locator {
  return page.locator(`uui-checkbox[aria-label="Select ${index}"]:visible`).first();
}
