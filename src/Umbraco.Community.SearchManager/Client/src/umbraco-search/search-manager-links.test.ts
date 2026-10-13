import { describe, expect, it, vi } from "vitest";

// The backoffice modules only exist inside Umbraco; stand in for the two path helpers.
vi.mock("@umbraco-cms/backoffice/settings", () => ({ UMB_SETTINGS_SECTION_PATHNAME: "settings" }));
vi.mock("@umbraco-cms/backoffice/workspace", () => ({
  UMB_WORKSPACE_VIEW_PATH_PATTERN: {
    generateAbsolute: (p: { sectionName: string; entityType: string; viewPathname: string }) =>
      `section/${p.sectionName}/workspace/${p.entityType}/view/${p.viewPathname}`,
  },
}));

const { indexFromQuery, searchManagerViewPath } = await import("./search-manager-links.js");

describe("searchManagerViewPath", () => {
  it("opens the view with the index in the query string", () => {
    expect(searchManagerViewPath("explore", "Umb_Content")).toBe(
      "section/settings/workspace/search-manager/view/explore?index=Umb_Content",
    );
  });

  it("appends extra query parameters after the index", () => {
    expect(searchManagerViewPath("diagnose", "ExternalIndex", { tool: "fields" })).toBe(
      "section/settings/workspace/search-manager/view/diagnose?index=ExternalIndex&tool=fields",
    );
  });

  it("keeps the named index when the extras carry another", () => {
    expect(searchManagerViewPath("explore", "ExternalIndex", new URLSearchParams("index=Other&page=2"))).toBe(
      "section/settings/workspace/search-manager/view/explore?index=ExternalIndex&page=2",
    );
  });
});

describe("indexFromQuery", () => {
  it("reads the index back", () => {
    expect(indexFromQuery("?index=Umb_Content")).toBe("Umb_Content");
  });

  it("gives undefined without one", () => {
    expect(indexFromQuery("")).toBeUndefined();
  });
});
