import { manifests as entrypoints } from "./entrypoints/manifest.js";
import { manifests as lang } from "./lang/manifests.js";
import { manifests as workspace } from "./workspace/manifests.js";
import { manifests as indexes } from "./indexes/manifests.js";
import { manifests as explore } from "./explore/manifests.js";
import { manifests as rebuilds } from "./rebuilds/manifests.js";
import { manifests as details } from "./details/manifests.js";
import { manifests as diagnose } from "./diagnose/manifests.js";
import { manifests as nodeAction } from "./node-action/manifests.js";

/**
 * Every manifest the package registers. `umbraco-package.json` loads this bundle, and each
 * feature folder contributes its own `manifests` array here.
 */
export const manifests: Array<UmbExtensionManifest> = [
  ...entrypoints,
  ...lang,
  ...workspace,
  ...indexes,
  ...explore,
  ...rebuilds,
  ...details,
  ...diagnose,
  ...nodeAction,
];
