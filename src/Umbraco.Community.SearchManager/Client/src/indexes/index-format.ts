import type { FailedRebuildSummary, IndexHealth } from "../api/types.gen.js";
import type { TermCall } from "../shared/term-call.js";

/** How a health value is drawn: a UUI icon name and a colour token, always beside its label. */
export interface HealthLook {
  /** The backoffice icon name. */
  icon: string;
  /** A `--uui-color-*` token for the icon. */
  color: string;
}

/**
 * Maps an index's health to its icon and colour. Every health is also written out as text, so
 * colour is never the only signal.
 *
 * @param health - The index's health.
 * @returns The icon and colour token.
 */
export function healthLook(health: IndexHealth): HealthLook {
  switch (health) {
    case "Healthy":
      return { icon: "icon-check", color: "var(--uui-color-positive)" };
    case "Rebuilding":
      return { icon: "icon-time", color: "var(--uui-color-warning-emphasis)" };
    case "Incomplete":
    case "Unhealthy":
      return { icon: "icon-alert", color: "var(--uui-color-warning-emphasis)" };
    case "Corrupted":
      return { icon: "icon-wrong", color: "var(--uui-color-danger)" };
    case "Empty":
    case "Unknown":
    default:
      return { icon: "icon-circle-dotted", color: "var(--uui-color-text-alt)" };
  }
}

/**
 * Formats a count for a table cell, or a dash when the source cannot count.
 *
 * @param value - The count, or null/undefined when unknown.
 * @param locale - The locale to group digits for.
 * @returns For example "3,412", or "-".
 */
export function formatCount(value: number | null | undefined, locale = "en-GB"): string {
  return value == null ? "-" : value.toLocaleString(locale);
}

/**
 * The line under an Incomplete index's health: how many documents the failed rebuild left and
 * why it failed. An unknown count or error is passed as an empty string, which the term leaves out.
 *
 * @param failed - The rebuild that left the index incomplete.
 * @param locale - The locale to group digits for.
 * @returns The term to localize.
 */
export function incompleteDetailTerm(failed: FailedRebuildSummary, locale = "en-GB"): TermCall {
  return [
    "searchManager_healthIncompleteDetail",
    failed.documentsWritten == null ? "" : formatCount(failed.documentsWritten, locale),
    failed.error ?? "",
  ];
}

/**
 * Formats a size in bytes with binary units and one decimal above a kilobyte.
 *
 * @param bytes - The size, or null/undefined when unknown.
 * @param locale - The locale for the decimal separator.
 * @returns For example "38.2 MB", "512 B", or "-".
 */
export function formatBytes(bytes: number | null | undefined, locale = "en-GB"): string {
  if (bytes == null) return "-";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  const digits = unit === 0 ? 0 : 1;
  return `${value.toLocaleString(locale, { minimumFractionDigits: digits, maximumFractionDigits: digits })} ${units[unit]}`;
}

/**
 * The display name of an index's engine.
 *
 * @param engine - The engine id the API returns.
 * @returns "Examine", "Umbraco Search", or the id itself for a source this client does not know.
 */
export function engineLabel(engine: string): string {
  switch (engine) {
    case "examine":
      return "Examine";
    case "umbraco-search":
      return "Umbraco Search";
    default:
      return engine;
  }
}
