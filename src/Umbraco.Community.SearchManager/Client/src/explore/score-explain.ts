import type {
  ExplainFactors,
  ExplainNode,
  ExplainNodeKind,
  ExplainRequest,
  ExploreSchema,
  ExploreTarget,
  Filter,
} from "../api/index.js";
import { describeFilter } from "./chip-format.js";
import type { Term } from "./filters.js";

/**
 * The kind of a contribution row: a node kind, or `QueryNorm`, the query normalisation factor
 * Lucene multiplies every word's weight by. `QueryNorm` rows hold that factor, not a share of the
 * score, and are left out of any total.
 */
export type ContributionKind = ExplainNodeKind | "QueryNorm";

/** One line of a flattened score: what added (or, for `Coord`, took away) how much. */
export interface Contribution {
  /** `kind`, `field` and `term` joined; rows with the same key are one row. */
  key: string;
  kind: ContributionKind;
  field: string | null;
  term: string | null;
  /** The share of the score; for `QueryNorm`, the factor itself. */
  value: number;
  /** The factors of the first node seen with this key. */
  factors: ExplainFactors | null;
  /** For a `Filter`, the index into the sent filters of the chip it came from. */
  filterIndex: number | null;
}

/** One line of a comparison of two scores. */
export interface ContributionDifference {
  key: string;
  kind: ContributionKind;
  field: string | null;
  term: string | null;
  filterIndex: number | null;
  /** Document A's share, 0 when A has no such row. */
  a: number;
  /** Document B's share, 0 when B has no such row. */
  b: number;
  /** `a - b`: positive when this row gives A the higher score, negative when it favours B. */
  difference: number;
}

/**
 * The query the last search sent (chips, raw Lucene and culture), which an explanation must run
 * again exactly: `filterIndex` and `mustNotMatch` index into its `filters`.
 */
export type SentQuery = Omit<ExplainRequest, "ids">;

/**
 * Why a target's scores cannot be explained, for the disabled "Explain score" button.
 *
 * @param target - The target; unknown while the targets load.
 * @param term - Localises the reason.
 * @returns The reason, or `undefined` when the target explains scores.
 */
export function explainUnavailableReason(target: ExploreTarget | undefined, term: Term): string | undefined {
  if (target?.capabilities.explainScore) return undefined;
  if (target?.engine === "umbraco-search") return term("searchManager_explainUmbracoSearch");
  return term("searchManager_explainNotLucene", target?.name ?? "");
}

/** Differences smaller than this are rounding noise and count as none. */
export const SCORE_EPSILON = 1e-6;

/** The node kinds that are leaves of a score: they become rows of their own. */
const LEAF_KINDS: ReadonlySet<ExplainNodeKind> = new Set(["TermMatch", "PhraseMatch", "Constant", "MatchAll"]);

/**
 * Flattens a score tree into the rows that make it up, so two scores can be compared field by
 * field: one row per word, phrase, prefix, match-all and filter, a `Coord` row for what scaling
 * for unmatched optional clauses took away, and a `QueryNorm` row with the normalisation factor.
 * Rows with the same kind, field and term are summed. The rows other than `QueryNorm` add up to
 * the root's value.
 *
 * @param node - The root of the explanation; `null` or `undefined` gives no rows.
 * @returns The rows, in the order first seen.
 */
export function flattenContributions(node: ExplainNode | null | undefined): Array<Contribution> {
  const rows = new Map<string, Contribution>();
  let queryNorm: number | undefined;

  const add = (kind: ContributionKind, source: ExplainNode | null, value: number): void => {
    const field = source?.field ?? null;
    const term = source?.term ?? null;
    const key = contributionKey(kind, field, term);
    const existing = rows.get(key);
    if (existing) {
      existing.value += value;
      return;
    }
    rows.set(key, {
      key,
      kind,
      field,
      term,
      value,
      factors: source?.factors ?? null,
      filterIndex: source?.filterIndex ?? null,
    });
  };

  const walk = (current: ExplainNode): void => {
    queryNorm ??= current.factors?.queryNorm ?? undefined;
    if (LEAF_KINDS.has(current.kind) || current.kind === "Filter") {
      // A filter is one row: its child only details how the clause matched.
      add(current.kind, current, current.value);
      return;
    }
    if (current.kind === "Coord") {
      current.children.forEach(walk);
      // The child's rows add up to the child's score; the coord row is what scaling took off it.
      const childTotal = current.children.reduce((total, child) => total + child.value, 0);
      add("Coord", null, current.value - childTotal);
      return;
    }
    if (current.children.length > 0) {
      current.children.forEach(walk);
      return;
    }
    // A clause the document does not meet adds nothing; anything else unknown keeps its value.
    if (current.kind !== "NoMatch" && current.value !== 0) add(current.kind, current, current.value);
  };

  if (!node) return [];
  walk(node);
  if (queryNorm !== undefined) add("QueryNorm", null, queryNorm);
  return [...rows.values()];
}

/**
 * Joins the rows of two scores by key, for the "Compare scores" table. A row only one document
 * has counts as 0 for the other. The `QueryNorm` row is the same for both, since both ran the
 * same query.
 *
 * @param a - Document A's rows, from {@link flattenContributions}.
 * @param b - Document B's rows.
 * @param differencesOnly - Whether to drop rows whose difference is within {@link SCORE_EPSILON}.
 * @returns The rows, largest difference (either way) first; equal differences keep A's order, then B's.
 */
export function compareExplanations(
  a: ReadonlyArray<Contribution>,
  b: ReadonlyArray<Contribution>,
  differencesOnly: boolean,
): Array<ContributionDifference> {
  const joined = new Map<string, ContributionDifference>();
  for (const [side, rows] of [
    ["a", a],
    ["b", b],
  ] as const) {
    for (const row of rows) {
      const entry = joined.get(row.key) ?? {
        key: row.key,
        kind: row.kind,
        field: row.field,
        term: row.term,
        filterIndex: row.filterIndex,
        a: 0,
        b: 0,
        difference: 0,
      };
      entry[side] = row.value;
      joined.set(row.key, entry);
    }
  }

  const rows = [...joined.values()].map((row) => ({ ...row, difference: row.a - row.b }));
  // Array.prototype.sort is stable, so ties keep the order first seen.
  return rows
    .filter((row) => !differencesOnly || Math.abs(row.difference) > SCORE_EPSILON)
    .sort((left, right) => Math.abs(right.difference) - Math.abs(left.difference));
}

/**
 * Formats a score or factor to three significant digits, for example `0.0123` or `4.21`.
 *
 * @param value - The number; `null` or `undefined` gives an empty string.
 * @returns The text.
 */
export function formatScore(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return "";
  // Very small values (rounding noise) read as zero, not as an exponent.
  if (Math.abs(value) < SCORE_EPSILON) return "0";
  return value.toLocaleString("en-GB", { maximumSignificantDigits: 3, useGrouping: false });
}

/**
 * Formats a difference with its sign, for example `+0.42` or `-1.3`.
 *
 * @param value - The difference.
 * @returns The text; `0` when within {@link SCORE_EPSILON}.
 */
export function formatDifference(value: number): string {
  const text = formatScore(value);
  return value > SCORE_EPSILON ? `+${text}` : text;
}

/** What a score sentence needs besides the node: the chips that were sent, and copy. */
export interface ExplainContext {
  /** The filters the search sent, in order; `filterIndex` and `mustNotMatch` index into it. */
  filters: ReadonlyArray<Filter>;
  /** The target's schema, for chip labels. */
  schema: ExploreSchema | undefined;
  /** Localises the copy. */
  term: Term;
}

/** The sentence for one node of a score tree, and an optional line of figures under it. */
export interface NodeSentence {
  text: string;
  detail?: string;
}

/**
 * Describes one node of a score tree in plain language: `nodeName contains "home" 2 times`, with
 * `tf 1.41 · idf 4.21 · field norm 0.31 · in 3 of 120 documents` under it.
 *
 * @param node - The node.
 * @param context - The sent chips and copy.
 * @returns The sentence and its figures.
 */
export function describeNode(node: ExplainNode, context: ExplainContext): NodeSentence {
  const { term } = context;
  const field = node.field ?? "";
  const word = node.term ?? "";
  const factors = node.factors ?? {};
  switch (node.kind) {
    case "Sum":
      return { text: term("searchManager_explainSum") };
    case "Coord":
      return {
        text: term("searchManager_explainCoord", formatCount(factors.matched), formatCount(factors.of)),
      };
    case "TermMatch":
      return {
        text: term("searchManager_explainTermMatch", field, word, formatTimes(factors.freq, term)),
        detail: factorLine(factors, term),
      };
    case "PhraseMatch":
      return {
        text: term("searchManager_explainPhraseMatch", field, word, formatTimes(factors.freq, term)),
        detail: factorLine(factors, term),
      };
    case "Constant": {
      // A trailing star is a prefix ("hood*"); anything else is a wildcard pattern.
      const prefix = /^[^*?]*\*$/.test(word) ? word.slice(0, -1) : undefined;
      return {
        text:
          prefix !== undefined
            ? term("searchManager_explainPrefix", field, prefix)
            : term("searchManager_explainWildcard", field, word),
        detail: factorLine(factors, term),
      };
    }
    case "MatchAll":
      return { text: term("searchManager_explainMatchAll") };
    case "Filter":
      return { text: term("searchManager_explainFilter", filterLabel(node, context)) };
    default:
      // NoMatch says which clause failed; Other is shown as the server worded it.
      return { text: node.description };
  }
}

/**
 * Describes a chip that removes documents rather than adding score: `must not match Document
 * type is product, adds no score`. A prohibiting chip that is not itself excluded (`level is
 * missing`) reads as a match that adds no score.
 *
 * @param index - The index into the sent filters, from `mustNotMatch`.
 * @param context - The sent chips and copy.
 * @returns The sentence, or `undefined` when the index names no sent chip.
 */
export function describeMustNotMatch(index: number, context: ExplainContext): string | undefined {
  const filter = context.filters[index];
  if (!filter) return undefined;
  if (filter.exclude) {
    const chip = describeFilter({ ...filter, exclude: false }, context.schema, context.term).label;
    return context.term("searchManager_explainMustNotMatch", chip);
  }
  return context.term("searchManager_explainNoScore", describeFilter(filter, context.schema, context.term).label);
}

/**
 * Describes a row of the "Compare scores" table: what the row is, without the per-document
 * figures (those are the A and B columns).
 *
 * @param row - The row.
 * @param context - The sent chips and copy.
 * @returns The text.
 */
export function describeContribution(row: ContributionDifference | Contribution, context: ExplainContext): string {
  const { term } = context;
  const field = row.field ?? "";
  const word = row.term ?? "";
  switch (row.kind) {
    case "QueryNorm":
      return term("searchManager_compareQueryNorm");
    case "Coord":
      return term("searchManager_compareCoord");
    case "TermMatch":
      return term("searchManager_compareTerm", field, word);
    case "PhraseMatch":
      return term("searchManager_comparePhrase", field, word);
    case "Constant":
      return term("searchManager_compareConstant", field, word);
    case "MatchAll":
      return term("searchManager_explainMatchAll");
    case "Filter":
      return term("searchManager_explainFilter", filterLabel(row, context));
    default:
      return word ? `${field}:${word}` : field || row.kind;
  }
}

/** The chip a filter clause came from, or its field and term when it maps to no sent chip. */
function filterLabel(
  source: { field?: string | null; term?: string | null; filterIndex?: number | null },
  context: ExplainContext,
): string {
  const filter = source.filterIndex != null ? context.filters[source.filterIndex] : undefined;
  if (filter) return describeFilter(filter, context.schema, context.term).label;
  return source.term ? `${source.field ?? ""}:${source.term}` : (source.field ?? "");
}

/** `tf 1.41 · idf 4.21 · field norm 0.31 · in 3 of 120 documents`, leaving out absent factors. */
function factorLine(factors: ExplainFactors, term: Term): string | undefined {
  const parts: Array<string> = [];
  if (factors.tf != null) parts.push(term("searchManager_explainTf", formatScore(factors.tf)));
  if (factors.idf != null) parts.push(term("searchManager_explainIdf", formatScore(factors.idf)));
  if (factors.fieldNorm != null) parts.push(term("searchManager_explainFieldNorm", formatScore(factors.fieldNorm)));
  if (factors.boost != null && factors.boost !== 1)
    parts.push(term("searchManager_explainBoost", formatScore(factors.boost)));
  if (factors.docFreq != null && factors.maxDocs != null)
    parts.push(term("searchManager_explainDocFreq", formatCount(factors.docFreq), formatCount(factors.maxDocs)));
  return parts.length > 0 ? parts.join(" · ") : undefined;
}

function formatTimes(freq: number | null | undefined, term: Term): string {
  return freq === 1 ? term("searchManager_explainOnce") : term("searchManager_explainTimes", formatScore(freq ?? 0));
}

function formatCount(value: number | null | undefined): string {
  return value == null ? "?" : value.toLocaleString("en-GB");
}

function contributionKey(kind: ContributionKind, field: string | null, term: string | null): string {
  return `${kind}\u0000${field ?? ""}\u0000${term ?? ""}`;
}
