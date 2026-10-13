import { expect, test } from "./support/test.js";
import { analyzerColumn, openAnalyzer } from "./support/search-manager.js";

/** Umbraco Search's draft content index (Examine provider). */
const CONTENT = "Umb_Content";

test("judges a free-text search on an Umbraco Search index's aggregated text", async ({ page }) => {
  await openAnalyzer(page, { text: "Café menu 2026: the opening hours", term: "open", left: CONTENT });

  const column = analyzerColumn(page, CONTENT);
  await expect(column.locator('li[data-final] [data-match="Prefix"]')).toContainText("opening");
  await expect(column.getByRole("status")).toHaveAttribute("data-verdict", "match");
});
