import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Filter, NativeValidation, NativeWarning } from "../api/index.js";
import {
  applyFix,
  chipsAfterNative,
  markPosition,
  nativeStart,
  NativeValidator,
  VALIDATE_DEBOUNCE_MS,
  type NativeCheck,
} from "./native-mode.js";

const warning: NativeWarning = {
  code: "needs_range",
  message: "level:3 never matches",
  start: 16,
  length: 1,
  replacement: "[3 TO 3]",
};

describe("applyFix", () => {
  it("puts the replacement in place of the warned text", () => {
    expect(applyFix("+umbraco +level:3", warning)).toBe("+umbraco +level:[3 TO 3]");
  });

  it("leaves the text alone when the warning no longer fits it", () => {
    expect(applyFix("level:3", warning)).toBe("level:3");
  });

  it("leaves the text alone when there is no fix", () => {
    expect(applyFix("+umbraco +level:3", { ...warning, replacement: null })).toBe("+umbraco +level:3");
  });
});

describe("markPosition", () => {
  it("marks the character at the position", () => {
    expect(markPosition("a ) b", 2)).toEqual({ before: "a ", at: ")", after: " b" });
  });

  it("marks a space after the text for an error at the end", () => {
    expect(markPosition("level:[3 TO", 11)).toEqual({ before: "level:[3 TO", at: " ", after: "" });
  });
});

describe("nativeStart", () => {
  it("starts from the query the last search ran", () => {
    expect(nativeStart("+umbraco")).toBe("+umbraco");
  });

  it("starts empty instead of the match-everything query", () => {
    expect(nativeStart("*:*")).toBe("");
  });
});

describe("chipsAfterNative", () => {
  it("keeps the chips that could not run after the ones read back", () => {
    const read: Filter = { kind: "Field", field: "level", op: "Equals", value: "3", exclude: false };
    const disabled: Filter = { kind: "Field", field: "colour", op: "Is", value: "red", exclude: false };
    expect(chipsAfterNative([read], [disabled])).toEqual([read, disabled]);
  });
});

describe("NativeValidator", () => {
  const valid: NativeValidation = { valid: true, error: null, position: null, warnings: [] };
  let checks: Array<NativeCheck>;

  beforeEach(() => {
    vi.useFakeTimers();
    checks = [];
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("reports pending, then the server's verdict after the wait", async () => {
    const validator = new NativeValidator(
      async () => ({ data: valid }),
      (check) => checks.push(check),
    );

    validator.check("level:3");
    await vi.advanceTimersByTimeAsync(VALIDATE_DEBOUNCE_MS);

    expect(checks).toEqual([{ status: "pending" }, { status: "result", result: valid }]);
  });

  it("sends only the last of several quick changes", async () => {
    const validate = vi.fn(async (_query: string) => ({ data: valid }));
    const validator = new NativeValidator(validate, () => {});

    validator.check("lev");
    validator.check("level:3");
    await vi.advanceTimersByTimeAsync(VALIDATE_DEBOUNCE_MS);

    expect(validate.mock.calls.map((call) => call[0])).toEqual(["level:3"]);
  });

  it("reports blank text as idle without asking the server", () => {
    const validate = vi.fn(async (_query: string) => ({ data: valid }));
    const validator = new NativeValidator(validate, (check) => checks.push(check));

    validator.check("  ");

    expect([checks, validate.mock.calls.length]).toEqual([[{ status: "idle" }], 0]);
  });

  it("reports a failed check as an error, not as invalid", async () => {
    const validator = new NativeValidator(
      async () => ({ error: { detail: "boom" } }),
      (check) => checks.push(check),
    );

    validator.check("a");
    await vi.advanceTimersByTimeAsync(VALIDATE_DEBOUNCE_MS);

    expect(checks.at(-1)).toEqual({ status: "error", message: "boom" });
  });
});
