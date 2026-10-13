/// <reference types="vitest/config" />
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import { umbracoPackageVersion } from "./scripts/umbraco-package-version.js";

export default defineConfig({
  plugins: [
    umbracoPackageVersion(fileURLToPath(new URL("../Umbraco.Community.SearchManager.csproj", import.meta.url))),
  ],
  build: {
    lib: {
      entry: {
        // The bundle entry registers every manifest the package provides.
        "umbraco-community-search-manager": "src/bundle.manifests.ts",
        // The box on Umbraco Search's index page; the Umbraco Search adapter registers it server
        // side, so it loads only where Umbraco Search is installed.
        "umbraco-search-index-box": "src/umbraco-search/search-index-box.element.ts",
      },
      formats: ["es"],
      fileName: (_format, entryName) => `${entryName}.js`,
    },
    // Build into the Razor Class Library's static web assets so the package serves it from /App_Plugins.
    outDir: "../wwwroot/App_Plugins/UmbracoCommunitySearchManager",
    emptyOutDir: true,
    sourcemap: true,
    rollupOptions: {
      // The backoffice provides @umbraco-cms/* (and Lit and UUI through it) at runtime.
      external: [/^@umbraco/],
    },
  },
  test: {
    environment: "happy-dom",
    include: ["src/**/*.test.ts", "scripts/**/*.test.ts", "e2e/support/**/*.test.ts"],
  },
});
