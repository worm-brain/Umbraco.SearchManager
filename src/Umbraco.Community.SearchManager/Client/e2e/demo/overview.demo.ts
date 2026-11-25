import { readFileSync } from "node:fs";
import type { Page } from "@playwright/test";
import { expect, test } from "../support/test.js";
import {
  analyzerColumn,
  openExplore,
  openIndexes,
  presenceRow,
  searchBox,
  selectBox,
  waitForSeededIndex,
} from "../support/search-manager.js";
import { repoRoot } from "../support/sites.js";
import { Demo } from "./overlay.js";

/**
 * The overview demo video (`bun run docs:demo -- overview`): the core loop from the Indexes view to Diagnose,
 * one numbered block per scene, each with its caption. Everything before the title card is load-in and is
 * cut from the video.
 */

/** The package icon's tile colour. */
const BRAND = "#B86E00";
const LOGO = `data:image/svg+xml;base64,${readFileSync(`${repoRoot}assets/icon/umbraco-searchmanager.svg`).toString("base64")}`;

/** Gets the site into the first scene's state: seeded indexes and an empty Rebuilds panel. */
async function preRoll(page: Page): Promise<void> {
  // Umbraco Search indexes the seed in the background after start-up; Find a node needs it there.
  await openExplore(page, "Umb_Content");
  await page.locator("uui-select#culture select").selectOption("en-US");
  await waitForSeededIndex(page, 10);
  await openExplore(page);
  await waitForSeededIndex(page);

  await openIndexes(page, "Umb_PublishedContent");
  const clear = page.getByRole("button", { name: "Clear finished" });
  if (await clear.isVisible()) {
    await clear.click();
    await expect(page.locator("search-manager-rebuilds-panel li")).toHaveCount(0);
  }
  await page.waitForLoadState("networkidle");
}

test("overview", async ({ page }) => {
  await preRoll(page);
  const demo = await Demo.start(page, { video: "overview", brand: BRAND, outDir: `${repoRoot}artifacts/demo` });

  // 1. Title card.
  await demo.titleCard({
    logo: LOGO,
    title: "Umbraco Search Manager",
    promise: "Every search index in one place: rebuild, explore and diagnose.",
  });

  // 2. Every index on one page.
  await demo.scene("Every index on one page, replacing the Examine dashboard");
  await demo.highlight(page.locator("search-manager-indexes-view uui-table").first());
  await demo.label(
    page.locator("search-manager-indexes-view uui-table-head-cell").filter({ hasText: "Health" }).first(),
    "Healthy or not, at a glance",
  );
  await demo.hold(4_000);

  // 3. Tick two indexes.
  await demo.scene("Tick several indexes to rebuild them in one go");
  await demo.click(selectBox(page, "ExternalIndex"));
  await demo.click(selectBox(page, "Umb_PublishedContent"));
  const rebuildTwo = page.getByRole("button", { name: "Rebuild 2 indexes" }).first();
  await demo.label(rebuildTwo, "One click, two indexes");
  await demo.hold(2_000);

  // 4. The confirmation.
  await demo.scene("The confirmation says what happens to live search first");
  await demo.click(rebuildTwo);
  const dialog = page.locator("uui-dialog-layout");
  await expect(dialog.getByText("Rebuild 2 indexes?")).toBeVisible();
  await demo.label(dialog.getByText("ExternalIndex", { exact: true }), "Cleared first", "right");
  await demo.label(dialog.getByText("Umb_PublishedContent", { exact: true }), "No downtime", "right");
  await demo.hold(5_500);

  // 5. Live progress.
  await demo.scene("Rebuilds run on the server and report progress live");
  await demo.click(dialog.locator("uui-button[look=primary]"));
  const panel = page.locator("search-manager-rebuilds-panel");
  await expect(panel).toBeVisible();
  await demo.highlight(panel);
  await demo.label(panel, "Keeps going if you leave the page");
  await demo.wait(async () => {
    for (const index of ["ExternalIndex", "Umb_PublishedContent"]) {
      await expect(
        panel
          .locator("li")
          .filter({ hasText: index })
          .getByText(/Done in/),
      ).toBeVisible({ timeout: 240_000 });
    }
  });
  await demo.hold(3_000);

  // 6. History.
  await demo.scene("Every rebuild is kept in the index's history");
  await demo.click(page.getByRole("button", { name: "Details and history for ExternalIndex" }));
  const history = page.locator("uui-box").filter({ hasText: "Rebuild history" });
  await expect(history.getByText("Succeeded").first()).toBeVisible();
  await demo.highlight(history.first());
  await demo.hold(5_000);
  await page.keyboard.press("Escape");
  await expect(history).toHaveCount(0);

  // 7. Explore with chips.
  await demo.scene("Words and field:value filters become chips");
  await demo.click(page.getByRole("tab", { name: "Explore" }));
  await expect(page.locator("search-manager-results").first()).toBeVisible();
  await demo.type(searchBox(page), "umbraco type:blogPost");
  await demo.press(searchBox(page), "Enter");
  await expect(page.locator("search-manager-filter-chip")).toHaveCount(2);
  await demo.label(page.locator("search-manager-filter-chip").filter({ hasText: "blogPost" }).first(), "Click to edit");
  await demo.label(page.locator("search-manager-fields-panel").first(), "Click a value to filter");
  await demo.hold(4_000);

  // 8. Show query.
  await demo.scene("Show query reveals the Lucene and C# behind the search");
  await demo.click(page.getByRole("button", { name: "Show query" }));
  const csharp = page
    .locator("search-manager-show-query-panel section.block")
    .filter({ hasText: "C# with Examine" })
    .first();
  await expect(csharp).toBeVisible();
  await demo.highlight(csharp);
  await demo.label(csharp.getByRole("button", { name: "Copy" }), "Paste it into your code");
  demo.poster();
  await demo.hold(5_000);

  // 9. The document drawer.
  await demo.scene("Open a result to see every stored field");
  await demo.click(page.getByRole("button", { name: "Hide query" }));
  await demo.click(page.locator("search-manager-results button.open").first());
  const drawer = page.locator("search-manager-document-drawer");
  await expect(drawer.getByText("__NodeId").first()).toBeVisible();
  await demo.label(drawer.getByRole("button", { name: /^Filter for / }).first(), "Turn any value into a chip");
  await demo.hold(4_000);

  // 10. Analyzer.
  await demo.scene("Analyzer shows why a search for cafe misses Café");
  await demo.click(page.getByRole("tab", { name: "Diagnose" }));
  await demo.click(page.getByRole("button", { name: "Analyzer" }));
  await demo.type(page.getByLabel("Text to index"), "Café menu 2026: the opening hours");
  await demo.type(page.getByLabel("A visitor searches for"), "cafe");
  const external = analyzerColumn(page, "ExternalIndex");
  const internal = analyzerColumn(page, "InternalIndex");
  await expect(external.getByRole("status")).toHaveAttribute("data-verdict", "no-match");
  await expect(internal.getByRole("status")).toHaveAttribute("data-verdict", "match");
  await demo.highlight(internal.getByText("ASCIIFoldingFilter", { exact: true }));
  await demo.label(external.getByRole("status").getByText("No match", { exact: true }), "No match", "right");
  await demo.label(internal.getByRole("status").getByText("Matches", { exact: true }), "Matches", "right");
  await demo.hold(6_000);

  // 11. Find a node.
  await demo.scene("Find a node shows which indexes hold a page");
  await demo.click(page.getByRole("button", { name: "Find a node" }));
  await demo.type(page.locator("search-manager-find-node uui-input.search input").first(), "Guide to Umbraco 1");
  await demo.click(page.locator('search-manager-find-node uui-ref-node[name="Guide to Umbraco 1"] #open-part').first());
  const presence = page.locator("search-manager-node-presence uui-table").first();
  await expect(presence).toBeVisible({ timeout: 30_000 });
  await demo.highlight(presence);
  await demo.label(presenceRow(page, "ExternalIndex").getByText("Present"), "Present", "right");
  await demo.hold(5_000);

  // 12. End card.
  await demo.endCard({
    install: "dotnet add package Umbraco.Community.SearchManager",
    docs: "github.com/worm-brain/Umbraco.SearchManager",
  });
  await demo.finish();
});
