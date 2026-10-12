import { manifests as entrypoints } from "./entrypoints/manifest.js";
import { manifests as lang } from "./lang/manifests.js";
import { manifests as workspace } from "./workspace/manifests.js";
import { manifests as indexes } from "./indexes/manifests.js";
import { manifests as explore } from "./explore/manifests.js";
import { manifests as rebuilds } from "./rebuilds/manifests.js";
import { manifests as details } from "./details/manifests.js";

/**
 * Every manifest the package registers. `umbraco-package.json` loads this bundle, and each
 * feature folder contributes its own `manifests` array here.
 *
 * The Diagnose tab (`diagnose/manifests.ts`) is left out until its first slice ships: an empty
 * placeholder tab is not worth a release.
 */
export const manifests: Array<UmbExtensionManifest> = [
  ...entrypoints,
  ...lang,
  ...workspace,
  ...indexes,
  ...explore,
  ...rebuilds,
  ...details,
];
