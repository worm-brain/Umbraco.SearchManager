import { expect, test } from "./support/test.js";
import { openIndexes } from "./support/search-manager.js";

test("lists the core Examine indexes and the custom index with their health", async ({ page }) => {
  await openIndexes(page);
  const table = page.getByRole("table", { name: "Search indexes" });

  // A fresh site indexes its seeded content in the background: reload until ExternalIndex fills.
  await expect(async () => {
    await page.reload();
    await expect(table.getByRole("row", { name: /ExternalIndex/ })).toContainText("Healthy", { timeout: 5_000 });
  }).toPass({ timeout: 180_000, intervals: [2_000, 5_000] });

  for (const index of ["InternalIndex", "MembersIndex", "ProductIndex"]) {
    await expect(table.getByText(index, { exact: true }).first()).toBeVisible();
  }
});

test("hides the core Examine Management dashboard from the Settings section", async ({ page }) => {
  await page.goto("/umbraco/section/settings");

  // Wait for the section's dashboard tabs before checking one is missing.
  await expect(page.getByRole("tab", { name: "Published Status" })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole("tab", { name: "Examine Management" })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "Search Manager" })).toBeVisible();
});
