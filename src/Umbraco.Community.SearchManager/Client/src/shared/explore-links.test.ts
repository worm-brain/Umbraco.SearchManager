import { describe, expect, it, vi } from "vitest";
import type { Filter } from "../api/index.js";
import { decodeExploreState } from "../explore/explore-state.js";

// The backoffice modules only exist inside Umbraco; stand in for the two path helpers.
vi.mock("@umbraco-cms/backoffice/settings", () => ({ UMB_SETTINGS_SECTION_PATHNAME: "settings" }));
vi.mock("@umbraco-cms/backoffice/workspace", () => ({
  UMB_WORKSPACE_VIEW_PATH_PATTERN: {
    generateAbsolute: (p: { sectionName: string; entityType: string; viewPathname: string }) =>
      `section/${p.sectionName}/workspace/${p.entityType}/view/${p.viewPathname}`,
  },
}));

const { exploreUrl, fieldStatisticsUrl } = await import("./explore-links.js");

/** Reads an Explore link back the way the Explore view does. */
function readBack(url: string) {
  return decodeExploreState(new URLSearchParams(url.slice(url.indexOf("?"))));
}

describe("exploreUrl", () => {
  it("opens Explore on the target with nothing else when no options are given", () => {
    expect(exploreUrl("ExternalIndex")).toBe(
      "section/settings/workspace/search-manager/view/explore?index=ExternalIndex",
    );
  });

  it("carries chips and the culture as Explore reads them", () => {
    const filters: Array<Filter> = [{ kind: "Field", field: "bodyText", op: "Missing", value: "", exclude: false }];

    const state = readBack(exploreUrl("Umb_Content", { filters, culture: "en-US" }));

    expect(state).toMatchObject({ target: "Umb_Content", filters, culture: "en-US" });
  });

  it("leaves an empty culture out", () => {
    expect(readBack(exploreUrl("ExternalIndex", { culture: "" })).culture).toBeUndefined();
  });
});

describe("fieldStatisticsUrl", () => {
  it("opens the fields tool of Diagnose on the index", () => {
    expect(fieldStatisticsUrl("ExternalIndex")).toBe(
      "section/settings/workspace/search-manager/view/diagnose?index=ExternalIndex&tool=fields",
    );
  });
});
