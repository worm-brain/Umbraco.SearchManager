import { UMB_EDIT_DOCUMENT_WORKSPACE_PATH_PATTERN } from "@umbraco-cms/backoffice/document";
import { UMB_EDIT_MEDIA_WORKSPACE_PATH_PATTERN } from "@umbraco-cms/backoffice/media";
import { UMB_EDIT_MEMBER_WORKSPACE_PATH_PATTERN } from "@umbraco-cms/backoffice/member";
import type { ExploreHit } from "../api/index.js";

/**
 * The backoffice route of a found item's editor, from its index type (`__IndexType`) and key,
 * built with the backoffice's own path patterns so it follows their routes.
 *
 * @param hit - A search hit.
 * @returns A path relative to the backoffice root, or `undefined` when the hit is not a
 *   content, media or member item with a key.
 */
export function editorPath(hit: Pick<ExploreHit, "entityType" | "key">): string | undefined {
  if (!hit.key) return undefined;
  switch (hit.entityType) {
    case "content":
      return UMB_EDIT_DOCUMENT_WORKSPACE_PATH_PATTERN.generateAbsolute({ unique: hit.key });
    case "media":
      return UMB_EDIT_MEDIA_WORKSPACE_PATH_PATTERN.generateAbsolute({ unique: hit.key });
    case "member":
      return UMB_EDIT_MEMBER_WORKSPACE_PATH_PATTERN.generateAbsolute({ unique: hit.key });
    default:
      return undefined;
  }
}
