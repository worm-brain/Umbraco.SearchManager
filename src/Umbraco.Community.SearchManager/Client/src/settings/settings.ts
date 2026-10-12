import { SettingsService, type SearchManagerSettings } from "../api/index.js";

/**
 * The core Examine Management dashboard (Settings section) and its two modals, the same in
 * Umbraco 17 and 18 (verified in `@umbraco-cms/backoffice` 17.0.0, 17.7.1, 18.0.0 and 18.2.1).
 */
export const CORE_EXAMINE_DASHBOARD_ALIASES = [
  "Umb.Dashboard.ExamineManagement",
  "Umb.Modal.Examine.FieldsSettings",
  "Umb.Modal.Examine.FieldsViewer",
] as const;

/**
 * The shape of `SettingsService.getSettings()` the loader depends on: hey-api's non-throwing
 * result, where a failed request sets `error` instead of `data`.
 */
export type FetchSettings = () => Promise<{ data?: SearchManagerSettings; error?: unknown }>;

/** The part of the extension registry {@link hideCoreExamineDashboardIfConfigured} needs. */
export interface ExtensionExcluder {
  exclude(alias: string): void;
}

/**
 * Fetches the client settings (`GET /settings`, ADR 0017).
 *
 * Never throws. A user without Settings access gets 403 here, which is expected rather than an
 * error worth surfacing, so every failure yields `undefined`.
 *
 * @param fetchSettings - The request to make; defaults to the generated client, tests pass a stub.
 * @returns The settings, or `undefined` when the request failed for any reason.
 */
export async function loadSettings(
  fetchSettings: FetchSettings = () => SettingsService.getSettings(),
): Promise<SearchManagerSettings | undefined> {
  try {
    const result = await fetchSettings();
    return result.error === undefined ? result.data : undefined;
  } catch {
    // fetch rejects on network failures and aborts rather than returning a result.
    return undefined;
  }
}

/**
 * Removes the core Examine Management dashboard and its modals when `HideCoreExamineDashboard`
 * is on (ADR 0017).
 *
 * Missing settings keep the dashboard: if Search Manager cannot reach its own API, the core
 * dashboard is the user's only way to manage indexes. The exclusion lives in the registry only,
 * so uninstalling the package brings the dashboard back with no clean-up.
 *
 * @param settings - The loaded settings, or `undefined` when loading failed.
 * @param registry - The backoffice extension registry.
 * @returns `true` when the dashboard was excluded.
 */
export function hideCoreExamineDashboardIfConfigured(
  settings: SearchManagerSettings | undefined,
  registry: ExtensionExcluder,
): boolean {
  if (!settings?.hideCoreExamineDashboard) return false;
  for (const alias of CORE_EXAMINE_DASHBOARD_ALIASES) registry.exclude(alias);
  return true;
}
