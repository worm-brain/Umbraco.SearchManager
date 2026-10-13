/**
 * A localization key and its arguments, chosen by a pure function and spread into
 * `localize.term(...call)` by the element, so the choice can be unit-tested without a backoffice.
 */
export type TermCall = [key: string, ...args: string[]];
