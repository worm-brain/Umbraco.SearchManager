import type {
  AnalysisResult,
  AnalysisToken,
  AnalyzerField,
  AnalyzerFieldsResult,
  SearchTermPart,
} from "../../api/index.js";
import type { Term } from "../../explore/filters.js";
import type { StatusLook } from "../node/node-model.js";

/*
 * The Analyzer tool's pure logic. It only formats what the server returns: the server runs the
 * index's own analyzer and query parser, so nothing here tokenizes or matches text itself.
 */

/** The longest text the server analyses; longer text is refused with a 400. */
export const MAX_ANALYZER_TEXT = 2000;

/** The longest search term the server takes; a longer one is refused with a 400. */
export const MAX_ANALYZER_TERM = 200;

/** The index the left column opens on when the URL names none. */
export const DEFAULT_LEFT_INDEX = "ExternalIndex";

/** The index the right column opens on when the URL names none. */
export const DEFAULT_RIGHT_INDEX = "InternalIndex";

/** The two indexes compared side by side. */
export interface AnalyzerSides {
  /** The left column's index; undefined only when there is no index at all. */
  left?: string;
  /** The right column's index; undefined only when there is no index at all. */
  right?: string;
}

/**
 * Finds an index by name, ignoring case (URLs are typed by hand), and returns its real name.
 *
 * @param names - The indexes to pick from.
 * @param wanted - The name asked for.
 * @returns The listed name, or undefined when it is not listed.
 */
function findName(names: ReadonlyArray<string>, wanted: string | undefined): string | undefined {
  const lower = wanted?.toLowerCase();
  return lower ? names.find((name) => name.toLowerCase() === lower) : undefined;
}

/**
 * The indexes the two columns show: the ones the URL names when they are listed, otherwise
 * ExternalIndex on the left and InternalIndex on the right (the analyzers most sites differ on),
 * otherwise the first two indexes. With a single index both columns show it.
 *
 * @param names - The index names, in picker order.
 * @param wanted - The indexes the URL names.
 * @returns The two indexes.
 */
export function analyzerSides(names: ReadonlyArray<string>, wanted: AnalyzerSides = {}): AnalyzerSides {
  const left = findName(names, wanted.left) ?? findName(names, DEFAULT_LEFT_INDEX) ?? names[0];
  const right =
    findName(names, wanted.right) ??
    (left?.toLowerCase() === DEFAULT_RIGHT_INDEX.toLowerCase() ? undefined : findName(names, DEFAULT_RIGHT_INDEX)) ??
    names.find((name) => name !== left) ??
    left;
  return { left, right };
}

/**
 * The field a column analyses: the one the URL names when the index has it, otherwise the
 * server's default field, otherwise the first field.
 *
 * @param result - The index's analyzable fields.
 * @param wanted - The field the URL names.
 * @returns The field name, or undefined when the index has no analyzable field.
 */
export function analyzerField(result: AnalyzerFieldsResult, wanted: string | undefined): string | undefined {
  const has = (name: string | null | undefined) => !!name && result.fields.some((field) => field.name === name);
  if (has(wanted)) return wanted;
  if (has(result.defaultField)) return result.defaultField ?? undefined;
  return result.fields[0]?.name;
}

/**
 * How a field reads in the picker: its label with the name in brackets when it has one
 * (`Title (title)`), otherwise the name.
 *
 * @param field - The field.
 * @returns The text.
 */
export function fieldDisplay(field: AnalyzerField): string {
  return field.label && field.label !== field.name ? `${field.label} (${field.name})` : field.name;
}

/**
 * The fields whose name or label contains the text typed in the picker, ignoring case, in the
 * server's order (Field first, then System, CultureVariant, RawCopy, Segment).
 *
 * @param fields - The index's fields.
 * @param search - The text typed in the picker.
 * @returns The matching fields; all of them for empty text.
 */
export function filterFields(fields: ReadonlyArray<AnalyzerField>, search: string): Array<AnalyzerField> {
  const needle = search.trim().toLowerCase();
  if (!needle) return [...fields];
  return fields.filter(
    (field) => field.name.toLowerCase().includes(needle) || (field.label ?? "").toLowerCase().includes(needle),
  );
}

/**
 * Why a request would be refused, checked before sending it so the server never answers 400:
 * empty text asks for text, overlong text or search says how long it may be.
 *
 * @param text - The text to index.
 * @param term - The search; may be empty.
 * @returns The lang key and arguments of the message, or undefined when the request can be sent.
 */
export function analyzerInputProblem(text: string, term: string): [key: string, ...args: Array<string>] | undefined {
  if (!text.trim()) return ["searchManager_analyzerNoText"];
  if (text.length > MAX_ANALYZER_TEXT) return ["searchManager_analyzerTextTooLong", String(MAX_ANALYZER_TEXT)];
  if (term.trim().length > MAX_ANALYZER_TERM) return ["searchManager_analyzerTermTooLong", String(MAX_ANALYZER_TERM)];
  return undefined;
}

/** How one token chip is drawn. */
export interface TokenLook {
  /** The chip's classes: `token` plus `removed`, `changed`, `added` and `match`. */
  className: string;
  /** Whether the text is struck through (the stage dropped the token). */
  struck: boolean;
  /** Whether the token is highlighted as one the search finds (final stage only). */
  marked: boolean;
  /** Lang keys of the visually-hidden words that say what colour and marks show. */
  labels: Array<string>;
}

/**
 * How a token chip is drawn. The state says what this stage did to the token; the match is only
 * a highlight and only counts on the final stage, the terms the index stores. The verdict comes
 * from the server's real query, not from these highlights.
 *
 * @param token - The token.
 * @param final - Whether the token is in the last stage.
 * @returns The classes, marks and hidden labels.
 */
export function tokenLook(token: AnalysisToken, final: boolean): TokenLook {
  const classes = ["token"];
  const labels: Array<string> = [];
  const struck = token.state === "Removed";
  if (struck) {
    classes.push("removed");
    labels.push("searchManager_analyzerTokenRemoved");
  } else if (token.state === "Changed") {
    classes.push("changed");
    labels.push("searchManager_analyzerTokenChanged");
  } else if (token.state === "Added") {
    classes.push("added");
    labels.push("searchManager_analyzerTokenAdded");
  }

  const marked = final && !struck && token.match !== "None";
  if (marked) {
    classes.push("match");
    labels.push(
      token.match === "Prefix"
        ? "searchManager_analyzerTokenPrefix"
        : token.match === "Pattern"
          ? "searchManager_analyzerTokenPattern"
          : "searchManager_analyzerTokenMatch",
    );
  }
  return { className: classes.join(" "), struck, marked, labels };
}

/**
 * How many positions are missing before each token: the gaps a removed stop word leaves. Removed
 * tokens are shown but do not count, so a later stage shows the gap the dropped word left.
 *
 * @param tokens - One stage's tokens, in order.
 * @returns For each token, the number of positions skipped just before it (0 for none).
 */
export function positionGaps(tokens: ReadonlyArray<AnalysisToken>): Array<number> {
  let previous: number | undefined;
  return tokens.map((token) => {
    if (token.state === "Removed") return 0;
    // Tokens on the same position (synonyms) are not a gap; positions start at 0.
    const gap = previous === undefined ? token.position : token.position - previous - 1;
    previous = Math.max(previous ?? token.position, token.position);
    return Math.max(gap, 0);
  });
}

/**
 * Describes one part of the search as words.
 *
 * @param part - The part.
 * @param term - Localises the wording.
 * @returns For example `words starting with opening`.
 */
function describePart(part: SearchTermPart, term: Term): string {
  switch (part.kind) {
    case "Word":
      return term("searchManager_analyzerPartWord", part.text);
    case "Prefix":
      return term("searchManager_analyzerPartPrefix", part.text);
    case "Wildcard":
      return term("searchManager_analyzerPartWildcard", part.text);
    case "Phrase":
      return term("searchManager_analyzerPartPhrase", part.text);
    case "Fuzzy":
      return term("searchManager_analyzerPartFuzzy", part.text);
    default:
      return term("searchManager_analyzerPartOther", part.text);
  }
}

/**
 * Joins items with a word: `a`, `a and b`, `a, b and c`.
 *
 * @param items - The items.
 * @param word - The joining word (`and`, `or`).
 * @returns The text.
 */
function joinWith(items: ReadonlyArray<string>, word: string): string {
  if (items.length <= 1) return items[0] ?? "";
  return `${items.slice(0, -1).join(", ")} ${word} ${items[items.length - 1]}`;
}

/**
 * The sentence that says what the search becomes, from the server's parts. Parts in one group are
 * alternatives (any one satisfies the group); a group is required when its parts are, otherwise it
 * only ranks higher; when no group is required, any group satisfies the search. For example:
 * `The search becomes: opening (or words starting with opening) and hours (or words starting with
 * hours); the exact phrase "opening hours" ranks higher.`
 *
 * @param parts - The search's parts.
 * @param term - Localises the wording.
 * @returns The sentence, or undefined when there are no parts.
 */
export function searchSentence(parts: ReadonlyArray<SearchTermPart>, term: Term): string | undefined {
  // Group the parts, in group order, keeping the server's order inside each group.
  const groups = new Map<number, Array<SearchTermPart>>();
  for (const part of [...parts].sort((a, b) => a.group - b.group)) {
    const group = groups.get(part.group) ?? [];
    group.push(part);
    groups.set(part.group, group);
  }
  if (groups.size === 0) return undefined;

  const describeGroup = (group: ReadonlyArray<SearchTermPart>) => {
    const [first, ...rest] = group.map((part) => describePart(part, term));
    return rest.length === 0
      ? first!
      : term("searchManager_analyzerPartAlternatives", first!, joinWith(rest, term("searchManager_analyzerJoinOr")));
  };

  // A ranking-only group that repeats a required word adds nothing to say: core Examine's
  // one-word search ranks the word as a phrase on top of requiring it.
  const requiredWords = new Set(parts.filter((part) => part.required && part.kind === "Word").map((part) => part.text));
  const repeatsRequiredWord = (group: ReadonlyArray<SearchTermPart>) =>
    group.every((part) => (part.kind === "Word" || part.kind === "Phrase") && requiredWords.has(part.text));

  const required: Array<string> = [];
  const optional: Array<string> = [];
  for (const group of groups.values()) {
    if (group.some((part) => part.required)) required.push(describeGroup(group));
    else if (!repeatsRequiredWord(group)) optional.push(describeGroup(group));
  }

  if (required.length === 0) {
    return term("searchManager_analyzerSearchBecomes", joinWith(optional, term("searchManager_analyzerJoinOr")));
  }
  const all = joinWith(required, term("searchManager_analyzerJoinAnd"));
  return optional.length === 0
    ? term("searchManager_analyzerSearchBecomes", all)
    : term("searchManager_analyzerSearchRanked", all, joinWith(optional, term("searchManager_analyzerJoinOr")));
}

/** What the verdict strip of a column shows. */
export type VerdictView =
  /** No search was typed: a prompt. */
  | { kind: "prompt" }
  /** A search was typed, but this field's searches are not judged: the server's reason. */
  | { kind: "unavailable"; reason: string }
  /** The server's verdict, with how it is drawn. */
  | { kind: "verdict"; matches: boolean; look: StatusLook };

/**
 * What the verdict strip shows for an analysis. The verdict is the server's (it ran the engine's
 * real query against the text), never worked out from the token highlights.
 *
 * @param result - The analysis.
 * @param term - The search typed; empty when none.
 * @returns The strip's content.
 */
export function verdictView(result: AnalysisResult, term: string): VerdictView {
  if (!term.trim()) return { kind: "prompt" };
  if (!result.search) {
    return { kind: "unavailable", reason: result.searchUnavailableReason ?? "" };
  }
  return result.search.matches
    ? {
        kind: "verdict",
        matches: true,
        look: { icon: "icon-check", label: "searchManager_analyzerMatches", color: "var(--uui-color-positive)" },
      }
    : {
        kind: "verdict",
        matches: false,
        look: { icon: "icon-wrong", label: "searchManager_analyzerNoMatch", color: "var(--uui-color-danger)" },
      };
}
