import type { UmbEntryPointOnInit, UmbEntryPointOnUnload } from "@umbraco-cms/backoffice/extension-api";
import { UMB_AUTH_CONTEXT } from "@umbraco-cms/backoffice/auth";
import { client } from "../api/client.gen.js";
import { hideCoreExamineDashboardIfConfigured, loadSettings } from "../settings/settings.js";

/**
 * Backoffice entry point for Search Manager.
 *
 * Runs once when the backoffice loads the package bundle. It points the generated Management API
 * client (`src/api`, from the `search-manager` OpenAPI document) at the backoffice's base URL and
 * auth, so every call carries the signed-in user's token. It then reads `GET /settings` and
 * removes the core Examine Management dashboard when `HideCoreExamineDashboard` is on (ADR 0017).
 *
 * @param host - The backoffice host element, used to consume the auth context.
 * @param extensionRegistry - The extension registry the core dashboard is excluded from.
 */
export const onInit: UmbEntryPointOnInit = (host, extensionRegistry) => {
  let settingsRequested = false;
  host.consumeContext(UMB_AUTH_CONTEXT, (authContext) => {
    // The auth context supplies a token getter that refreshes as needed, the API base URL and
    // the credentials mode the backoffice itself uses.
    const config = authContext?.getOpenApiConfiguration();
    client.setConfig({
      auth: config?.token ?? undefined,
      baseUrl: config?.base ?? "",
      credentials: config?.credentials ?? "same-origin",
    });

    // The settings request needs the configured client, so it waits for the auth context; the
    // flag stops a re-provided context from fetching twice. `exclude` also removes an extension
    // that is already registered, so the dashboard goes even if it rendered first.
    if (!authContext || settingsRequested) return;
    settingsRequested = true;
    void loadSettings().then((settings) => hideCoreExamineDashboardIfConfigured(settings, extensionRegistry));
  });
};

/**
 * Called when the backoffice unloads the package. The entry point holds no resources, so there
 * is nothing to release; the backoffice's entry-point module type requires the export.
 *
 * @param _host - The backoffice host element.
 * @param _extensionRegistry - The extension registry.
 */
export const onUnload: UmbEntryPointOnUnload = (_host, _extensionRegistry) => {};
