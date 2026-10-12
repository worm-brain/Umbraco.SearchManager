/**
 * Stand-ins for backoffice APIs that arrived after the package's floor (Umbraco 17.0.0 /
 * 18.0.0). The CI floor typecheck builds the client against the oldest backoffice of each major,
 * so code here must only use what 17.0.0 already had.
 */

/**
 * One option of a `uui-select`. `UUISelectOption` is only exported from
 * `@umbraco-cms/backoffice/external/uui` in later 17.x releases; this has the same shape as
 * uui-select's global `Option`.
 */
export interface SelectOption {
  name: string;
  value: string;
  group?: string;
  selected?: boolean;
  disabled?: boolean;
}

/**
 * The format `UmbLocalizationController.dateTime()` uses (a short date with a medium time), for
 * `this.localize.date(value, DATE_TIME)`: `dateTime()` itself does not exist in 17.0.0.
 */
export const DATE_TIME: Intl.DateTimeFormatOptions = { dateStyle: "short", timeStyle: "medium" };
