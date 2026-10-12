import { expect, test } from "./support/test.js";
import { openIndexes, selectBox } from "./support/search-manager.js";

/**
 * Indexes this spec rebuilds. No other spec searches them, so a rebuild in progress never empties
 * an index another test is reading.
 */
const INDEXES = ["InternalIndex", "ProductIndex"] as const;

test("rebuilds two indexes to completion and records them in the history", async ({ page }) => {
  test.setTimeout(240_000);
  await openIndexes(page, "ProductIndex");

  // Tick both indexes, then confirm the bulk rebuild.
  for (const index of INDEXES) {
    await selectBox(page, index).click();
  }
  await page.getByRole("button", { name: "Rebuild 2 indexes" }).first().click();
  await expect(page.getByText("Rebuild 2 indexes?")).toBeVisible();
  await page.locator("uui-dialog-layout uui-button[look=primary]").click();

  // The Rebuilds panel follows both runs through to "Done in ...".
  const panel = page.locator("search-manager-rebuilds-panel");
  for (const index of INDEXES) {
    await expect(
      panel
        .locator("li")
        .filter({ hasText: index })
        .getByText(/Done in/),
    ).toBeVisible({
      timeout: 180_000,
    });
  }

  // The details sidebar lists the run in the index's rebuild history.
  await page.getByRole("button", { name: "Details and history for ProductIndex" }).click();
  const history = page.locator("uui-box").filter({ hasText: "Rebuild history" });
  await expect(history.getByText("Succeeded").first()).toBeVisible();
  await expect(history.getByText(/from Search Manager/).first()).toBeVisible();
});
