/** Manifests for the package's backoffice entry point. */
export const manifests: Array<UmbExtensionManifest> = [
  {
    name: "Umbraco Community Search Manager Entrypoint",
    alias: "Umbraco.Community.SearchManager.Entrypoint",
    type: "backofficeEntryPoint",
    js: () => import("./entrypoint.js"),
  },
];
