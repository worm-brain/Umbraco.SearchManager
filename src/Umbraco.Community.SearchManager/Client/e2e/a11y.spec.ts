// axe reports no serious or critical accessibility issues on the main views: Indexes (with the
// rebuild confirmation open), Explore (with chips and with the document drawer open) and Diagnose's
// three tools.
import AxeBuilder from "@axe-core/playwright";
import type { Page } from "@playwright/test";
import { expect, test } from "./support/test.js";
import {
  findNode,
  openAnalyzer,
  openExplore,
  openFieldStatistics,
  openIndexes,
  searchBox,
  selectBox,
  waitForSeededIndex,
} from "./support/search-manager.js";

/** The impacts that fail the check; minor and moderate findings are not enforced. */
const FAILING_IMPACTS = new Set(["serious", "critical"]);

/**
 * Runs axe on the page and returns its serious and critical violations, one line per rule with
 * the offending targets, so a failure reads as a list.
 *
 * The whole page is scanned, backoffice chrome included, because `AxeBuilder.include` cannot
 * select inside the backoffice's nested shadow roots. Nothing is excluded today; add any exclusion
 * as a narrow `exclude()` with the reason beside it.
 *
 * @param page - The page to scan, once its view has settled.
 * @returns One line per serious or critical violation; empty when there are none.
 */
async function seriousViolations(page: Page): Promise<Array<string>> {
  // Let loader bars and late requests finish so axe sees the settled view.
  await page.waitForLoadState("networkidle");
  await expect(page.locator("uui-loader-bar")).toHaveCount(0);
  const results = await new AxeBuilder({ page }).analyze();
  return results.violations
    .filter((violation) => FAILING_IMPACTS.has(violation.impact ?? ""))
    .map(
      (violation) =>
        `${violation.impact} ${violation.id}: ${violation.help} -> ${violation.nodes
          .map((node) => JSON.stringify(node.target))
          .join(", ")}`,
    );
}

test.describe("axe", () => {
  test("finds no serious issues on the Indexes view", async ({ page }) => {
    await openIndexes(page);

    expect(await seriousViolations(page)).toEqual([]);
  });

  test("finds no serious issues with the rebuild confirmation open", async ({ page }) => {
    await openIndexes(page);
    await selectBox(page, "ExternalIndex").click();
    await page.getByRole("button", { name: "Rebuild 1 index" }).first().click();
    await expect(page.locator("uui-dialog-layout")).toBeVisible();

    expect(await seriousViolations(page)).toEqual([]);
  });

  test("finds no serious issues on the Explore view with chips", async ({ page }) => {
    await openExplore(page);
    await waitForSeededIndex(page);
    await searchBox(page).fill("umbraco type:blogPost");
    await searchBox(page).press("Enter");
    await expect(page.locator("search-manager-filter-chip")).toHaveCount(2);

    expect(await seriousViolations(page)).toEqual([]);
  });

  test("finds no serious issues with the document drawer open", async ({ page }) => {
    await openExplore(page);
    await waitForSeededIndex(page);
    await page.locator("search-manager-results button.open").first().click();
    await expect(page.locator("search-manager-document-drawer").getByText("__NodeId").first()).toBeVisible();

    expect(await seriousViolations(page)).toEqual([]);
  });

  test("finds no serious issues on Diagnose's field statistics", async ({ page }) => {
    await openFieldStatistics(page);

    expect(await seriousViolations(page)).toEqual([]);
  });

  test("finds no serious issues on Diagnose's Find a node", async ({ page }) => {
    await openExplore(page);
    await waitForSeededIndex(page);
    // A content page seeded by samples/Shared/SampleContent/SampleContentSeeder.cs.
    await findNode(page, "Guide to Umbraco 1");

    expect(await seriousViolations(page)).toEqual([]);
  });

  test("finds no serious issues on Diagnose's analyzer", async ({ page }) => {
    await openAnalyzer(page, { text: "Café menu 2026: the opening hours", term: "cafe" });

    expect(await seriousViolations(page)).toEqual([]);
  });
});
