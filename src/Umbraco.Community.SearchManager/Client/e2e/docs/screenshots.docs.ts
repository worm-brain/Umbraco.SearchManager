import { expect, test } from "../support/test.js";
import {
  analyzerColumn,
  findNode,
  openExplore,
  openFieldStatistics,
  openAnalyzer,
  openIndexes,
  searchBox,
  selectBox,
  waitForSeededIndex,
} from "../support/search-manager.js";
import { repoRoot } from "../support/sites.js";
import type { Page } from "@playwright/test";

/**
 * The documentation screenshots, written to `docs/images/`. Run with `bun run docs:screenshots`
 * (playwright.docs.config.ts). Each test sets up one view the docs show and saves it; they run in
 * order on one worker.
 */

/** Saves the viewport to `docs/images/{name}.png`, after the view has settled. */
async function shoot(page: Page, name: string): Promise<void> {
  // Let transitions, popovers and late requests finish so a shot never catches a half-drawn view.
  await page.waitForLoadState("networkidle");
  await page.waitForTimeout(500);
  await page.screenshot({ path: `${repoRoot}docs/images/${name}.png` });
}

/**
 * Opens the Indexes view with an empty Rebuilds panel: runs left over from earlier work (or an
 * earlier screenshot run) would crowd the table out of the shot.
 */
async function openCleanIndexes(page: Page, index: string): Promise<void> {
  await openIndexes(page, index);
  const clear = page.getByRole("button", { name: "Clear finished" });
  if (await clear.isVisible()) {
    await clear.click();
    await expect(page.locator("search-manager-rebuilds-panel li")).toHaveCount(0);
  }
}

test("rebuild confirmation", async ({ page }) => {
  await openCleanIndexes(page, "Umb_PublishedContent");
  await selectBox(page, "ExternalIndex").click();
  await selectBox(page, "Umb_PublishedContent").click();
  await page.getByRole("button", { name: "Rebuild 2 indexes" }).first().click();
  await expect(page.getByText("Rebuild 2 indexes?")).toBeVisible();
  await shoot(page, "rebuild-confirm");
});

test("rebuilds panel and history", async ({ page }) => {
  test.setTimeout(300_000);
  await openCleanIndexes(page, "ProductIndex");
  // Umb_Media and Umb_Members are empty on the sample site, so the panel shows "nothing to index",
  // and the indexes shot that follows shows them succeeded.
  for (const index of ["ProductIndex", "InternalIndex", "Umb_Media", "Umb_Members"]) {
    await selectBox(page, index).click();
  }
  await page.getByRole("button", { name: "Rebuild 4 indexes" }).first().click();
  await page.locator("uui-dialog-layout uui-button[look=primary]").click();

  const panel = page.locator("search-manager-rebuilds-panel");
  await expect(
    panel
      .locator("li")
      .filter({ hasText: "Umb_Members" })
      .getByText(/Done in/),
  ).toBeVisible({ timeout: 240_000 });
  await shoot(page, "rebuilds");

  await page.getByRole("button", { name: "Details and history for ProductIndex" }).click();
  await expect(
    page.locator("uui-box").filter({ hasText: "Rebuild history" }).getByText("Succeeded").first(),
  ).toBeVisible();
  await shoot(page, "index-details");
});

test("indexes", async ({ page }) => {
  await openCleanIndexes(page, "Umb_PublishedContent");
  await shoot(page, "indexes");
});

test("explore with chips and the fields panel", async ({ page }) => {
  await openExplore(page);
  await waitForSeededIndex(page);
  await searchBox(page).fill("umbraco type:blogPost");
  await searchBox(page).press("Enter");
  await expect(page.locator("search-manager-filter-chip")).toHaveCount(2);
  await shoot(page, "explore");
});

test("field suggestions", async ({ page }) => {
  await openExplore(page);
  await waitForSeededIndex(page);
  await searchBox(page).fill("type:");
  await expect(page.getByText("Values of", { exact: false }).first()).toBeVisible();
  await shoot(page, "suggestions");
});

test("document drawer", async ({ page }) => {
  await openExplore(page);
  await waitForSeededIndex(page);
  await searchBox(page).fill("type:blogPost");
  await searchBox(page).press("Enter");
  await page.locator("search-manager-results button.open").first().click();
  await expect(page.locator("search-manager-document-drawer").getByText("__NodeId").first()).toBeVisible();
  await shoot(page, "document-drawer");
});

test("show query", async ({ page }) => {
  await openExplore(page);
  await waitForSeededIndex(page);
  await searchBox(page).fill("umbraco type:blogPost");
  await searchBox(page).press("Enter");
  await page.getByRole("button", { name: "Show query" }).click();
  await expect(page.getByText("C# with Examine").first()).toBeVisible();
  await shoot(page, "show-query");
});

test("native mode", async ({ page }) => {
  await openExplore(page);
  await waitForSeededIndex(page);
  await page.getByRole("button", { name: "Lucene", exact: true }).click();
  await page.locator("search-manager-native-box uui-input input").fill("+__NodeTypeAlias:blogpost and level:2");
  await expect(page.getByText(/^Fix:/).first()).toBeVisible();
  await shoot(page, "native-mode");
});

test("diagnose field statistics", async ({ page }) => {
  await openExplore(page);
  await waitForSeededIndex(page);
  await openFieldStatistics(page);
  await shoot(page, "diagnose-fields");
});

test("diagnose analyzer", async ({ page }) => {
  await openAnalyzer(page, { text: "Café menu 2026: the opening hours", term: "cafe" });
  await expect(analyzerColumn(page, "ExternalIndex").getByRole("status")).toHaveAttribute("data-verdict", "no-match");
  await expect(analyzerColumn(page, "InternalIndex").getByRole("status")).toHaveAttribute("data-verdict", "match");
  await shoot(page, "diagnose-analyzer");
});

test("find a node", async ({ page }) => {
  await openExplore(page);
  await waitForSeededIndex(page);
  // Umbraco Search indexes the seeded content after start-up; wait so its rows show Present.
  await openExplore(page, "Umb_Content");
  await page.locator("uui-select#culture select").selectOption("en-US");
  await waitForSeededIndex(page, 10);
  await findNode(page, "Guide to Umbraco 1");
  const match = page.locator("search-manager-node-match");
  await match.locator("uui-select#match-index select").selectOption("ExternalIndex");
  await match.locator("uui-input.query input").fill("umbraco type:blogPost");
  await match.locator("uui-input.query input").press("Enter");
  await expect(match.locator(".verdict")).toBeVisible();
  await shoot(page, "find-a-node");
});

test("show in search indexes", async ({ page }) => {
  await openExplore(page, "Umb_Content");
  await page.locator("uui-select#culture select").selectOption("en-US");
  await waitForSeededIndex(page, 10);
  await page.goto("/umbraco/section/content");
  const home = page.locator('uui-menu-item[label="Home"]').first();
  await expect(home).toBeVisible({ timeout: 30_000 });
  await home.hover();
  await page.getByRole("button", { name: "View actions for 'Home'" }).first().click();
  await page.locator('uui-menu-item[label="Show in search indexes"]').first().click();
  await expect(page.locator("search-manager-node-presence-modal search-manager-node-presence uui-table")).toBeVisible({
    timeout: 30_000,
  });
  await shoot(page, "show-in-search-indexes");
});

test("explore an Umbraco Search index", async ({ page }) => {
  await openExplore(page, "Umb_PublishedContent");
  await page.locator("uui-select#culture select").selectOption("en-US");
  await waitForSeededIndex(page, 10);
  await searchBox(page).fill('umbraco "getting started"');
  await searchBox(page).press("Enter");
  await expect(page.locator("search-manager-filter-chip")).toHaveCount(2);
  await shoot(page, "explore-umbraco-search");
});

test("Umbraco Search index page box", async ({ page }) => {
  await page.goto("/umbraco/section/settings/workspace/search-index/edit/Umb_PublishedContent");
  await expect(page.locator("uui-box").filter({ hasText: "Search Manager" })).toBeVisible();
  await shoot(page, "umbraco-search-box");
});
