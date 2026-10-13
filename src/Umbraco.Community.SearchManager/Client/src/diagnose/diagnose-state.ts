/** The tools of the Diagnose view, as the `tool` query-string key names them. */
export const DIAGNOSE_TOOLS = ["fields", "node"] as const;

/** One Diagnose tool: field statistics, or find a node. */
export type DiagnoseTool = (typeof DIAGNOSE_TOOLS)[number];

/** The tool a view opens on when the URL names none (or one it does not know). */
export const DEFAULT_DIAGNOSE_TOOL: DiagnoseTool = "fields";

/**
 * Everything that defines what the Diagnose view shows, kept in the URL so a link reopens it.
 * Each tool reads the keys it needs and leaves the others alone.
 */
export interface DiagnoseViewState {
  /** The tool shown. */
  tool: DiagnoseTool;
  /**
   * The index; `undefined` picks the default one. Written as `index`, the key every Search
   * Manager link uses (`searchManagerViewPath`), so `?index=` links from the Umbraco Search box
   * keep working.
   */
  index?: string;
  /** Field statistics: whether system fields, culture variants, raw copies and segments show. */
  variants: boolean;
  /** Field statistics: the field-name filter text. */
  filter?: string;
  /** Find a node: the node key. */
  node?: string;
  /** Find a node: the culture. */
  culture?: string;
  /** Find a node: the query to check the node against. */
  q?: string;
}

/** The query-string keys the view owns; other keys in the URL are kept as they are. */
export const DIAGNOSE_STATE_KEYS = ["tool", "index", "variants", "filter", "node", "culture", "q"] as const;

/**
 * An empty view: the default tool and index, Field-group rows only.
 *
 * @returns A fresh state.
 */
export function createDiagnoseState(): DiagnoseViewState {
  return { tool: DEFAULT_DIAGNOSE_TOOL, variants: false };
}

/**
 * Writes the state as query-string parameters, leaving out defaults (the default tool, variants
 * off, empty text).
 *
 * @param state - The state.
 * @returns The parameters, in a stable order.
 */
export function encodeDiagnoseState(state: DiagnoseViewState): URLSearchParams {
  const params = new URLSearchParams();
  if (state.tool !== DEFAULT_DIAGNOSE_TOOL) params.set("tool", state.tool);
  if (state.index) params.set("index", state.index);
  if (state.variants) params.set("variants", "1");
  if (state.filter) params.set("filter", state.filter);
  if (state.node) params.set("node", state.node);
  if (state.culture) params.set("culture", state.culture);
  if (state.q) params.set("q", state.q);
  return params;
}

/**
 * Reads the state from query-string parameters. Never throws: an unknown tool or a missing value
 * falls back to its default on its own, so one bad value does not discard a shared link.
 *
 * @param params - The query string.
 * @returns The state.
 */
export function decodeDiagnoseState(params: URLSearchParams): DiagnoseViewState {
  const tool = params.get("tool");
  return {
    tool: (DIAGNOSE_TOOLS as ReadonlyArray<string>).includes(tool ?? "")
      ? (tool as DiagnoseTool)
      : DEFAULT_DIAGNOSE_TOOL,
    index: params.get("index") || undefined,
    variants: params.get("variants") === "1" || params.get("variants") === "true",
    filter: params.get("filter") || undefined,
    node: params.get("node") || undefined,
    culture: params.get("culture") || undefined,
    q: params.get("q") || undefined,
  };
}

/**
 * Replaces the view's keys in a query string and keeps every other key.
 *
 * @param search - The current query string, with or without `?`.
 * @param state - The encoded state.
 * @returns The merged query string without `?`.
 */
export function mergeDiagnoseIntoSearch(search: string, state: URLSearchParams): string {
  const merged = new URLSearchParams(search);
  for (const key of DIAGNOSE_STATE_KEYS) merged.delete(key);
  for (const [key, value] of state) merged.set(key, value);
  return merged.toString();
}
