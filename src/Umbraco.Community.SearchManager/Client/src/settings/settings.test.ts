import { describe, expect, it } from "vitest";
import type { SearchManagerSettings } from "../api/index.js";
import { hideCoreExamineDashboardIfConfigured, loadSettings, type ExtensionExcluder } from "./settings.js";

const defaults: SearchManagerSettings = { hideCoreExamineDashboard: true };

/** A registry fake that records which aliases were excluded. */
function fakeRegistry(): ExtensionExcluder & { excluded: Array<string> } {
  const excluded: Array<string> = [];
  return { excluded, exclude: (alias) => void excluded.push(alias) };
}

describe("loadSettings", () => {
  it("returns the settings when the request succeeds", async () => {
    expect(await loadSettings(async () => ({ data: defaults }))).toEqual(defaults);
  });

  it("returns undefined when the server responds with an error", async () => {
    expect(await loadSettings(async () => ({ error: { title: "Forbidden" } }))).toBeUndefined();
  });

  it("returns undefined instead of throwing when the request rejects", async () => {
    const settings = await loadSettings(async () => {
      throw new TypeError("Failed to fetch");
    });

    expect(settings).toBeUndefined();
  });
});

describe("hideCoreExamineDashboardIfConfigured", () => {
  it("excludes the core dashboard and its two modals when the setting is on", () => {
    const registry = fakeRegistry();

    hideCoreExamineDashboardIfConfigured(defaults, registry);

    expect(registry.excluded).toEqual([
      "Umb.Dashboard.ExamineManagement",
      "Umb.Modal.Examine.FieldsSettings",
      "Umb.Modal.Examine.FieldsViewer",
    ]);
  });

  it("keeps the core dashboard when the setting is off", () => {
    const registry = fakeRegistry();

    hideCoreExamineDashboardIfConfigured({ hideCoreExamineDashboard: false }, registry);

    expect(registry.excluded).toEqual([]);
  });

  it("keeps the core dashboard when the settings could not be loaded", () => {
    const registry = fakeRegistry();

    hideCoreExamineDashboardIfConfigured(undefined, registry);

    expect(registry.excluded).toEqual([]);
  });
});
