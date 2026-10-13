import type {
  ClauseMatch,
  Filter,
  IndexPresence,
  NodeCulture,
  NodeMatchResult,
  PresenceStatus,
} from "../../api/index.js";

/** How a status or outcome is drawn: always an icon and a text, never colour alone. */
export interface StatusLook {
  /** The `umb-icon` name. */
  icon: string;
  /** The lang key of the text beside the icon. */
  label: string;
  /** A UUI colour token for the icon. */
  color: string;
}

/**
 * How an index's presence status is drawn.
 *
 * @param status - The status.
 * @returns The icon, label key and colour.
 */
export function presenceLook(status: PresenceStatus): StatusLook {
  switch (status) {
    case "Present":
      return { icon: "icon-check", label: "searchManager_nodePresent", color: "var(--uui-color-positive)" };
    case "Missing":
      return { icon: "icon-alert", label: "searchManager_nodeMissing", color: "var(--uui-color-danger)" };
    default:
      return { icon: "icon-block", label: "searchManager_nodeNotApplicable", color: "var(--uui-color-text-alt)" };
  }
}

/**
 * The indexes a query can be tested against: those that hold the node.
 *
 * @param indexes - Every index's presence.
 * @returns The present ones, in the server's order.
 */
export function matchableIndexes(indexes: ReadonlyArray<IndexPresence>): Array<IndexPresence> {
  return indexes.filter((index) => index.status === "Present");
}

/** What a "View fields" link opens Explore with. */
export interface ViewFieldsLink {
  /** The index. */
  target: string;
  /** The chip that finds the node's documents. */
  filters: Array<Filter>;
  /** The document to open in the drawer. */
  doc?: string;
  /** The culture to search in (set for Umbraco Search). */
  culture?: string;
}

/**
 * The Explore link of a row that holds the node: a chip that finds the node's documents, the
 * document to open, and the culture. Only present rows with a chip from the server get one.
 *
 * @param presence - One index's presence.
 * @returns The link inputs, or `undefined` when there is nothing to view.
 */
export function viewFieldsLink(presence: IndexPresence): ViewFieldsLink | undefined {
  if (presence.status !== "Present" || !presence.viewFilter) return undefined;
  return {
    target: presence.target,
    filters: [presence.viewFilter],
    doc: presence.viewDocumentId || undefined,
    culture: presence.viewCulture || undefined,
  };
}

/**
 * The cultures an index holds the node in, as one line.
 *
 * @param cultures - The index's cultures (lower case for core Examine, ISO for Umbraco Search).
 * @returns For example `en-us, da-dk`, or empty for an invariant node.
 */
export function culturesText(cultures: ReadonlyArray<string>): string {
  return cultures.join(", ");
}

/**
 * The lang key of a node culture's state: published, published with unpublished changes, or
 * not published.
 *
 * @param culture - The culture.
 * @returns The lang key.
 */
export function cultureStateKey(culture: NodeCulture): string {
  if (!culture.published) return "searchManager_nodeCultureUnpublished";
  return culture.draft ? "searchManager_nodeCultureDraft" : "searchManager_nodeCulturePublished";
}

/**
 * The overall verdict of a match test.
 *
 * @param result - The server's answer.
 * @returns The icon, label key and colour.
 */
export function verdictLook(result: NodeMatchResult): StatusLook {
  if (!result.inIndex) {
    return { icon: "icon-block", label: "searchManager_matchNotInIndex", color: "var(--uui-color-text-alt)" };
  }
  return result.matches
    ? { icon: "icon-check", label: "searchManager_matchYes", color: "var(--uui-color-positive)" }
    : { icon: "icon-wrong", label: "searchManager_matchNo", color: "var(--uui-color-danger)" };
}

/** A chip's outcome in a match test. */
export type ClauseOutcome = "matched" | "notMatched" | "skipped";

/**
 * A chip's outcome: `matched` is null when the chip was skipped (it could not run there).
 *
 * @param clause - The chip's result; undefined when the server returned none for it.
 * @returns The outcome.
 */
export function clauseOutcome(clause: ClauseMatch | undefined): ClauseOutcome {
  if (clause?.matched === true) return "matched";
  if (clause?.matched === false) return "notMatched";
  return "skipped";
}

/**
 * How a chip's outcome is drawn.
 *
 * @param outcome - The outcome.
 * @returns The icon, label key and colour.
 */
export function clauseLook(outcome: ClauseOutcome): StatusLook {
  switch (outcome) {
    case "matched":
      return { icon: "icon-check", label: "searchManager_clauseMatched", color: "var(--uui-color-positive)" };
    case "notMatched":
      return { icon: "icon-wrong", label: "searchManager_clauseNotMatched", color: "var(--uui-color-danger)" };
    default:
      return { icon: "icon-block", label: "searchManager_clauseSkipped", color: "var(--uui-color-text-alt)" };
  }
}

/**
 * Pairs each sent chip with its result, by the chip's position.
 *
 * @param filters - The chips sent, in order.
 * @param result - The server's answer.
 * @returns One entry per chip.
 */
export function pairClauses(
  filters: ReadonlyArray<Filter>,
  result: NodeMatchResult,
): Array<{ filter: Filter; clause: ClauseMatch | undefined }> {
  return filters.map((filter, index) => ({
    filter,
    clause: result.clauses.find((clause) => clause.index === index),
  }));
}

/**
 * The index a match test runs on: the one asked for when it holds the node, otherwise the first
 * that does.
 *
 * @param indexes - Every index's presence.
 * @param wanted - The index picked, if any.
 * @returns The index name, or `undefined` when no index holds the node.
 */
export function matchTarget(indexes: ReadonlyArray<IndexPresence>, wanted: string | undefined): string | undefined {
  const present = matchableIndexes(indexes);
  return (present.find((index) => index.target === wanted) ?? present[0])?.target;
}
