import { describe, expect, it } from "vitest";
import { readSeedSignal } from "./seed-log.js";

describe("readSeedSignal", () => {
  it("reads the seeder's completion line", () => {
    expect(readSeedSignal("[16:01:02 INF] Seeded sample content for Search Manager")).toBe("seeded");
  });

  it("reads Umbraco's stalled cache instruction sync", () => {
    expect(
      readSeedSignal(
        "[16:01:02 ERR] Cache instruction sync did not complete within 00:01:00 and may be stalled on a hung database connection.",
      ),
    ).toBe("hung");
  });

  it("ignores every other line", () => {
    expect(readSeedSignal("[16:01:02 INF] Seeding sample content for Search Manager")).toBeNull();
  });
});
