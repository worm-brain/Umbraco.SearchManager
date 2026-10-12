/**
 * What a key press in the search input does:
 *
 * - `submit`: Enter with text to parse.
 * - `removeLastChip`: Backspace in an empty input while chips exist.
 * - `clear`: Escape with text in the input.
 *
 * Anything else is left to the browser.
 */
export type SearchKeyAction = "submit" | "removeLastChip" | "clear";

/**
 * Maps a key press to its {@link SearchKeyAction}.
 *
 * @param event - The key and whether an IME composition is in progress (Enter that confirms a
 *   composition must not submit).
 * @param value - The input's text.
 * @param chipCount - How many chips the box holds.
 * @returns The action, or `undefined` to let the key through.
 */
export function searchKeyAction(
  event: Pick<KeyboardEvent, "key" | "isComposing">,
  value: string,
  chipCount: number,
): SearchKeyAction | undefined {
  if (event.isComposing) return undefined;
  switch (event.key) {
    case "Enter":
      return value.trim().length > 0 ? "submit" : undefined;
    case "Backspace":
      return value.length === 0 && chipCount > 0 ? "removeLastChip" : undefined;
    case "Escape":
      return value.length > 0 ? "clear" : undefined;
    default:
      return undefined;
  }
}
