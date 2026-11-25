import { describe, expect, it } from "vitest";
import { calloutPlacement, escapeHtml, highlightCode } from "./overlay.js";

describe("escapeHtml", () => {
  it("escapes markup so a caption shows as text", () => {
    expect(escapeHtml(`<b title="x">Tom & Jerry's</b>`)).toBe(
      "&lt;b title=&quot;x&quot;&gt;Tom &amp; Jerry&#39;s&lt;/b&gt;",
    );
  });
});

describe("highlightCode", () => {
  it("colours C# keywords, strings and comments", () => {
    expect(highlightCode('var name = "Crm"; // the section', "csharp")).toBe(
      '<span class="k">var</span> name = <span class="s">&quot;Crm&quot;</span>; <span class="c">// the section</span>',
    );
  });

  it("does not colour a keyword inside a string", () => {
    expect(highlightCode('"new value"', "csharp")).toBe('<span class="s">&quot;new value&quot;</span>');
  });

  it("treats # as a comment in shell snippets", () => {
    expect(highlightCode("umbraco doctor # check the profile", "shell")).toBe(
      'umbraco doctor <span class="c"># check the profile</span>',
    );
  });

  it("escapes markup in the code", () => {
    expect(highlightCode("List<int> ids", "csharp")).toBe('List&lt;<span class="k">int</span>&gt; ids');
  });
});

describe("calloutPlacement", () => {
  const viewport = { width: 1440, height: 810 };
  const size = { width: 200, height: 40 };

  it("puts the label below a target with room under it, centred on the target", () => {
    const placement = calloutPlacement({ x: 600, y: 100, width: 100, height: 30 }, size, viewport);

    expect(placement).toEqual({ left: 550, top: 144, side: "below", arrow: 100 });
  });

  it("puts the label above a target too close to the caption bar", () => {
    const placement = calloutPlacement({ x: 600, y: 650, width: 100, height: 30 }, size, viewport);

    expect(placement).toEqual({ left: 550, top: 596, side: "above", arrow: 100 });
  });

  it("keeps the label inside the viewport and points the arrow at a target near the edge", () => {
    const placement = calloutPlacement({ x: 0, y: 100, width: 20, height: 30 }, size, viewport);

    expect(placement).toEqual({ left: 16, top: 144, side: "below", arrow: 16 });
  });

  it("puts the label beside a target when asked, vertically centred on it", () => {
    const placement = calloutPlacement({ x: 600, y: 100, width: 100, height: 30 }, size, viewport, "right");

    expect(placement).toEqual({ left: 714, top: 95, side: "right", arrow: 20 });
  });

  it("falls back to below when there is no room on the right", () => {
    const placement = calloutPlacement({ x: 1300, y: 100, width: 100, height: 30 }, size, viewport, "right");

    expect(placement).toEqual({ left: 1224, top: 144, side: "below", arrow: 126 });
  });
});
