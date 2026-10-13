import { expect, test } from "./support/test.js";
import { findNode, openExplore, presenceRow, waitForSeededIndex } from "./support/search-manager.js";

// Seeded by samples/Shared/SampleContent/SampleContentSeeder.cs:
// - "Guide to Umbraco 1" (i = 0): a content page published in en-US and da-DK.
// - "Notes on Umbraco #1" (i = 0): a blog post; every seventh post, this one included, is unpublished.
// - "Guide to content modelling 5" (i = 4): published in en-US, with an unpublished da-DK draft.
const PUBLISHED_PAGE = "Guide to Umbraco 1";
const UNPUBLISHED_POST = "Notes on Umbraco #1";
const DANISH_DRAFT = "Guide to content modelling 5";

test.beforeEach(async ({ page }) => {
  await openExplore(page);
  await waitForSeededIndex(page);
});

test("shows a published page in the content indexes and not applicable to members", async ({ page }) => {
  await findNode(page, PUBLISHED_PAGE);

  await expect(presenceRow(page, "ExternalIndex")).toContainText("Present");
  await expect(presenceRow(page, "InternalIndex")).toContainText("Present");
  await expect(presenceRow(page, "MembersIndex")).toContainText("Not applicable");
  await expect(presenceRow(page, "ExternalIndex").locator("uui-button")).toBeVisible();
});

test("explains why an unpublished post is missing from the published index", async ({ page }) => {
  await findNode(page, UNPUBLISHED_POST);

  const external = presenceRow(page, "ExternalIndex");
  await expect(external).toContainText("Missing");
  await expect(external).toContainText(/not published/i);
  await expect(presenceRow(page, "InternalIndex")).toContainText("Present");
});

test("holds only the published culture of a page with a draft in the published index", async ({ page }) => {
  await findNode(page, DANISH_DRAFT);

  const external = presenceRow(page, "ExternalIndex");
  await expect(external).toContainText("en-us");
  await expect(external).not.toContainText("da-dk");
  const internal = presenceRow(page, "InternalIndex");
  await expect(internal).toContainText("en-us");
  await expect(internal).toContainText("da-dk");
});

test("tells which filter of a query misses the node, with the node's value", async ({ page }) => {
  await findNode(page, PUBLISHED_PAGE);
  const match = page.locator("search-manager-node-match");
  await match.locator("uui-select#match-index select").selectOption("ExternalIndex");

  await match.locator("uui-input.query input").fill("umbraco type:blogPost");
  await match.locator("uui-input.query input").press("Enter");

  await expect(match.locator(".verdict")).toContainText("Does not match");
  await expect(match.locator('li[data-clause="0"]')).toContainText(/(^|\s)matched/);
  await expect(match.locator('li[data-clause="0"]')).not.toContainText("not matched");
  const typeChip = match.locator('li[data-clause="1"]');
  await expect(typeChip).toContainText("not matched");
  await expect(typeChip).toContainText(/contentPage/i);
  // The query is written to the URL, so a link reopens and reruns the test.
  await expect(page).toHaveURL(/[?&]q=/);
});

test("opens the presence sidebar from a document's actions in the content tree", async ({ page }) => {
  await page.goto("/umbraco/section/content");
  const home = page.locator('uui-menu-item[label="Home"]').first();
  await expect(home).toBeVisible({ timeout: 30_000 });
  await home.hover();
  await page.getByRole("button", { name: "View actions for 'Home'" }).first().click();
  await page.locator('uui-menu-item[label="Show in search indexes"]').first().click();

  const sidebar = page.locator("search-manager-node-presence-modal");
  await expect(sidebar.locator("search-manager-node-presence uui-table")).toBeVisible({ timeout: 30_000 });
  await expect(presenceRow(page, "ExternalIndex")).toContainText("Present");
});
