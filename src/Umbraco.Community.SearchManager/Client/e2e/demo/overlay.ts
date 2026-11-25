import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import type { Locator, Page } from "@playwright/test";
import { isFastForward, type DemoCaption, type DemoSpan, type DemoTimeline } from "../../scripts/demo-timeline.js";

/**
 * The demo-video overlay: records a page with Playwright's screencast and draws captions, callout labels,
 * a highlight, a visible cursor and full-frame cards on top of it. Identical in every package repo
 * (package standard, demo videos): nothing here names this package.
 *
 * Everything visible is drawn in the page, in one `popover` element re-raised on every update so it stays
 * above backoffice modals (which open in the top layer). The node side keeps the whole overlay state and
 * re-sends it on each change, so the overlay survives a navigation. Times are `Date.now()`, the clock the
 * renderer (`scripts/demo-render.ts`) expects in `timeline.json`.
 */

/** A rectangle in CSS pixels of the viewport. */
export interface Box {
  x: number;
  y: number;
  width: number;
  height: number;
}

/** Where a callout label goes, from {@link calloutPlacement}. */
export interface CalloutPlacement {
  left: number;
  top: number;
  /** Which side of the target the label sits on; the arrow points the other way. */
  side: "below" | "above" | "right";
  /**
   * The arrow's position inside the label, pointing at the target's centre: from the left edge for a label
   * below or above, from the top edge for a label on the right.
   */
  arrow: number;
}

/** Options for {@link Demo.start}. */
export interface DemoOptions {
  /** The video's name: `overview`, or a feature clip's slug. Names the output folder. */
  video: string;
  /** The package's brand colour (from its icon), used for the highlight ring and label borders. */
  brand: string;
  /** The folder the video's folder is created in, normally `<repo>/artifacts/demo`. */
  outDir: string;
  /** Frame size in pixels; matches the viewport. Defaults to 1920x1080. */
  size?: { width: number; height: number };
  /** Page zoom, so the backoffice reads larger on video. Defaults to 4/3 (it then lays out at 1440x810). */
  zoom?: number;
}

/** The title card's content. */
export interface TitleCard {
  /** The package logo as a data URL or an `https:` URL. */
  logo: string;
  title: string;
  /** The one-line promise from the storyboard. */
  promise: string;
}

/** The end card's content. */
export interface EndCard {
  /** The install command, e.g. `dotnet add package ...`. */
  install: string;
  /** The documentation URL. */
  docs: string;
}

/** Height kept free at the bottom of the viewport for the caption bar. */
const CAPTION_RESERVE = 110;
/** Space between a target and its label. */
const LABEL_GAP = 14;
/** Distance a label keeps from the viewport's edges. */
const EDGE_MARGIN = 16;
/** How far the highlight ring sits outside its target. */
const SPOT_PADDING = 6;

/**
 * Escapes text for HTML, so captions, labels and code are always shown as text.
 *
 * @param text - Any text.
 * @returns The text with `&`, `<`, `>`, `"` and `'` escaped.
 */
export function escapeHtml(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

/** Keywords coloured by {@link highlightCode}, per language. */
const KEYWORDS: Record<string, ReadonlyArray<string>> = {
  csharp: [
    "using",
    "namespace",
    "public",
    "private",
    "internal",
    "static",
    "class",
    "record",
    "new",
    "var",
    "return",
    "await",
    "async",
    "void",
    "string",
    "int",
    "bool",
    "true",
    "false",
    "null",
    "this",
  ],
  json: ["true", "false", "null"],
  shell: [],
};

/**
 * Colours a code snippet for a code card: comments, strings, keywords and numbers become spans with the
 * classes `c`, `s`, `k` and `n`; everything is HTML-escaped. Deliberately small: a card shows a few lines,
 * not a file.
 *
 * @param code - The snippet.
 * @param language - `csharp`, `json` or `shell`; anything else gets strings and numbers only.
 * @returns HTML for the inside of a `<pre>`.
 */
export function highlightCode(code: string, language: string): string {
  const keywords = KEYWORDS[language] ?? [];
  const comment = language === "shell" ? "#.*$" : "\\/\\/.*$";
  const keyword = keywords.length ? `\\b(?:${keywords.join("|")})\\b` : "(?!)";
  // One alternation per token kind, in priority order: a comment or string swallows anything inside it.
  const token = new RegExp(
    `(${comment})|("(?:\\\\.|[^"\\\\])*"|'(?:\\\\.|[^'\\\\])*')|(${keyword})|(\\b\\d+(?:\\.\\d+)?\\b)`,
    "gm",
  );

  let html = "";
  let last = 0;
  for (const match of code.matchAll(token)) {
    html += escapeHtml(code.slice(last, match.index));
    const kind = match[1] ? "c" : match[2] ? "s" : match[3] ? "k" : "n";
    html += `<span class="${kind}">${escapeHtml(match[0])}</span>`;
    last = match.index + match[0].length;
  }
  return html + escapeHtml(code.slice(last));
}

/**
 * Places a callout label next to its target. By default it goes below the target when it fits above the
 * caption bar, otherwise above it, centred on the target. With `prefer: "right"` it goes beside the target,
 * vertically centred, for wide targets such as a table row whose centre is empty space; it falls back to the
 * default when there is no room on the right. The label always stays inside the viewport.
 *
 * @param target - The target element's box.
 * @param size - The label's rendered size.
 * @param viewport - The viewport size; the bottom {@link CAPTION_RESERVE} pixels are kept for the caption.
 * @param prefer - `"auto"` (below or above) or `"right"`.
 * @returns The label's position and its arrow's offset.
 */
export function calloutPlacement(
  target: Box,
  size: { width: number; height: number },
  viewport: { width: number; height: number },
  prefer: "auto" | "right" = "auto",
): CalloutPlacement {
  const rightLeft = target.x + target.width + LABEL_GAP;
  if (prefer === "right" && rightLeft + size.width <= viewport.width - EDGE_MARGIN) {
    const middle = target.y + target.height / 2;
    const top = Math.min(
      Math.max(EDGE_MARGIN, middle - size.height / 2),
      viewport.height - CAPTION_RESERVE - size.height,
    );
    return { left: rightLeft, top, side: "right", arrow: Math.min(Math.max(12, middle - top), size.height - 12) };
  }

  const belowTop = target.y + target.height + LABEL_GAP;
  const fitsBelow = belowTop + size.height <= viewport.height - CAPTION_RESERVE;
  const side = fitsBelow ? "below" : "above";
  const top = fitsBelow ? belowTop : Math.max(EDGE_MARGIN, target.y - LABEL_GAP - size.height);

  const centre = target.x + target.width / 2;
  const left = Math.min(Math.max(EDGE_MARGIN, centre - size.width / 2), viewport.width - EDGE_MARGIN - size.width);
  // Keep the arrow on the label even when the target is near an edge.
  const arrow = Math.min(Math.max(16, centre - left), size.width - 16);
  return { left, top, side, arrow };
}

/** The overlay state the page renders; the node side owns it and sends it whole. */
interface OverlayState {
  css: string;
  cursor: { x: number; y: number; ms: number; pulse: number };
  caption: string | null;
  spot: Box | null;
  labels: string;
  card: string | null;
  badge: boolean;
}

/** The overlay's styles. `--brand` is set from {@link DemoOptions.brand}. */
function overlayCss(brand: string): string {
  return `
#demo-overlay { --brand: ${brand}; position: fixed; inset: 0; width: 100vw; height: 100vh; margin: 0; padding: 0;
  border: 0; background: transparent; overflow: visible; pointer-events: none; z-index: 2147483647;
  font-family: Lato, "Segoe UI", system-ui, sans-serif; color: #1b264f; }
#demo-overlay .spot { position: fixed; border-radius: 10px; border: 3px solid var(--brand);
  box-shadow: 0 0 0 200vmax rgb(10 14 30 / 0.42); transition: all 300ms ease; opacity: 0; }
#demo-overlay .spot.on { opacity: 1; }
#demo-overlay .label { position: fixed; max-width: 340px; padding: 10px 14px; border-radius: 10px; background: #fff;
  border: 2px solid var(--brand); box-shadow: 0 8px 24px rgb(0 0 0 / 0.18); font-size: 17px; font-weight: 700;
  line-height: 1.3; animation: demo-pop 220ms ease-out; }
#demo-overlay .label .arrow { position: absolute; width: 14px; height: 14px; background: #fff;
  border: 2px solid var(--brand); transform: translateX(-50%) rotate(45deg); }
#demo-overlay .label.below .arrow { top: -9px; border-right: 0; border-bottom: 0; }
#demo-overlay .label.above .arrow { bottom: -9px; border-left: 0; border-top: 0; }
#demo-overlay .label.right .arrow { left: -9px; border-top: 0; border-right: 0; transform: translateY(-50%) rotate(45deg); }
#demo-overlay .card.on ~ .cursor, #demo-overlay .card.on ~ .pulse { display: none; }
#demo-overlay .caption { position: fixed; left: 50%; bottom: 28px; transform: translateX(-50%); max-width: 1100px;
  padding: 14px 28px; border-radius: 12px; background: rgb(20 24 40 / 0.88); color: #fff; font-size: 26px;
  font-weight: 700; line-height: 1.3; text-align: center; opacity: 0; transition: opacity 250ms ease; }
#demo-overlay .caption.on { opacity: 1; }
#demo-overlay .card { position: fixed; inset: 0; display: grid; place-items: center; background: #f4f5f8;
  opacity: 0; transition: opacity 400ms ease; }
#demo-overlay .card.on { opacity: 1; }
#demo-overlay .card .inner { display: grid; justify-items: center; gap: 22px; text-align: center; }
#demo-overlay .card img { width: 128px; height: 128px; }
#demo-overlay .card h1 { margin: 0; font-size: 52px; font-weight: 900; }
#demo-overlay .card p { margin: 0; max-width: 900px; font-size: 28px; color: #3e4566; }
#demo-overlay .card .bar { width: 96px; height: 6px; border-radius: 3px; background: var(--brand); }
#demo-overlay .card code { padding: 14px 24px; border-radius: 10px; background: #1b1f2e; color: #fff;
  font: 600 26px/1.3 "Cascadia Code", Consolas, monospace; }
#demo-overlay .card pre { margin: 0; padding: 28px 36px; border-radius: 14px; background: #1b1f2e; color: #e6e8ef;
  font: 500 21px/1.5 "Cascadia Code", Consolas, monospace; text-align: left; max-width: 1200px; white-space: pre; }
#demo-overlay pre .k { color: #c792ea; } #demo-overlay pre .s { color: #c3e88d; }
#demo-overlay pre .c { color: #7f88a8; font-style: italic; } #demo-overlay pre .n { color: #f78c6c; }
#demo-overlay .badge { position: fixed; top: 24px; right: 24px; padding: 8px 16px; border-radius: 999px;
  background: rgb(20 24 40 / 0.88); color: #fff; font-size: 20px; font-weight: 700; display: none; }
#demo-overlay .badge.on { display: block; }
#demo-overlay .cursor { position: fixed; left: 0; top: 0; width: 28px; height: 28px; transition-property: transform;
  transition-timing-function: cubic-bezier(0.4, 0, 0.2, 1); filter: drop-shadow(0 2px 3px rgb(0 0 0 / 0.35)); }
#demo-overlay .pulse { position: fixed; width: 36px; height: 36px; margin: -18px 0 0 -18px; border-radius: 50%;
  border: 3px solid var(--brand); opacity: 0; }
#demo-overlay .pulse.go { animation: demo-pulse 450ms ease-out; }
@keyframes demo-pop { from { opacity: 0; transform: translateY(4px); } }
@keyframes demo-pulse { from { opacity: 1; transform: scale(0.4); } to { opacity: 0; transform: scale(1.4); } }
`;
}

/**
 * Renders the overlay state in the page. Runs inside the browser, so it must not close over anything.
 *
 * @param s - The whole overlay state.
 */
function renderOverlay(s: OverlayState): void {
  let root = document.getElementById("demo-overlay");
  if (!root) {
    root = document.createElement("div");
    root.id = "demo-overlay";
    root.setAttribute("popover", "manual");
    root.innerHTML =
      `<style></style><div class="spot"></div><div class="labels"></div><div class="card"></div>` +
      `<div class="caption"></div><div class="badge">&#9193; Fast-forward</div><div class="pulse"></div>` +
      `<svg class="cursor" viewBox="0 0 24 24"><path d="M4 2l15 11-6.5 1.2L16 21l-3 1.4-3.4-6.9L4 20z" ` +
      `fill="#fff" stroke="#111" stroke-width="1.5" stroke-linejoin="round"/></svg>`;
    document.body.append(root);
  }
  // Re-raise above any modal opened since the last update: the newest popover is on top of the top layer.
  if (root.matches(":popover-open")) root.hidePopover();
  root.showPopover();

  const q = (selector: string): HTMLElement => root.querySelector(selector) as HTMLElement;
  const style = q("style");
  if (style.textContent !== s.css) style.textContent = s.css;

  const spot = q(".spot");
  // A new highlight fades in where it belongs; only one that is already showing glides to a new target.
  spot.style.transitionProperty = spot.classList.contains("on") ? "all" : "opacity";
  spot.classList.toggle("on", !!s.spot);
  if (s.spot)
    Object.assign(spot.style, {
      left: `${s.spot.x}px`,
      top: `${s.spot.y}px`,
      width: `${s.spot.width}px`,
      height: `${s.spot.height}px`,
    });

  const labels = q(".labels");
  if (labels.innerHTML !== s.labels) labels.innerHTML = s.labels;

  const card = q(".card");
  if (s.card !== null && card.dataset.html !== s.card) {
    card.innerHTML = s.card;
    card.dataset.html = s.card;
  }
  card.classList.toggle("on", s.card !== null);

  const caption = q(".caption");
  if (s.caption !== null) caption.textContent = s.caption;
  caption.classList.toggle("on", s.caption !== null);

  q(".badge").classList.toggle("on", s.badge);

  const cursor = q(".cursor");
  cursor.style.transitionDuration = `${s.cursor.ms}ms`;
  cursor.style.transform = `translate(${s.cursor.x - 4}px, ${s.cursor.y - 2}px)`;

  const pulse = q(".pulse");
  if (pulse.dataset.n !== String(s.cursor.pulse)) {
    pulse.dataset.n = String(s.cursor.pulse);
    Object.assign(pulse.style, { left: `${s.cursor.x}px`, top: `${s.cursor.y}px` });
    pulse.classList.remove("go");
    void pulse.offsetWidth; // restart the animation
    if (s.cursor.pulse > 0) pulse.classList.add("go");
  }
}

/**
 * Measures a label's rendered size inside the overlay, in the overlay's own CSS pixels (offset sizes are not
 * affected by the page zoom). Runs inside the browser.
 *
 * @param html - The label's inner HTML.
 * @returns Its width and height.
 */
function measureLabel(html: string): { width: number; height: number } {
  const root = document.getElementById("demo-overlay")!;
  const probe = document.createElement("div");
  probe.className = "label";
  probe.style.visibility = "hidden";
  probe.innerHTML = html;
  root.append(probe);
  const size = { width: probe.offsetWidth, height: probe.offsetHeight };
  probe.remove();
  return size;
}

/**
 * Zooms the page like the browser's zoom, so the backoffice reads larger in the video while the frames
 * keep the viewport's full resolution. Runs inside the browser.
 *
 * @param zoom - The zoom factor.
 */
function zoomPage(zoom: number): void {
  document.documentElement.style.zoom = String(zoom);
}

/** A label pinned to a target, measured again on every update. */
interface PinnedLabel {
  target: Locator;
  text: string;
  prefer: "auto" | "right";
}

/**
 * One demo recording. Start it once the page shows the first scene's starting state; everything before
 * {@link Demo.titleCard} is cut from the video as load-in.
 *
 * Highlight and label targets are kept as locators and measured again on every update, so they follow the
 * page when it scrolls or re-lays out (a later label scrolling its own target into view, say).
 */
export class Demo {
  private readonly frames: Array<{ file: string; t: number }> = [];
  private readonly captions: Array<DemoCaption> = [];
  private readonly fastForwards: Array<DemoSpan> = [];
  private readonly labels: Array<PinnedLabel> = [];
  private readonly labelSizes = new Map<string, { width: number; height: number }>();
  private spotTarget: Locator | null = null;
  private readonly state: OverlayState;
  private keepStart: number | undefined;
  private keepEnd: number | undefined;
  private posterAt: number | undefined;

  private constructor(
    private readonly page: Page,
    private readonly options: Required<DemoOptions>,
    private readonly dir: string,
  ) {
    const viewport = this.viewport();
    this.state = {
      css: overlayCss(options.brand),
      cursor: { x: viewport.width / 2, y: viewport.height / 2, ms: 0, pulse: 0 },
      caption: null,
      spot: null,
      labels: "",
      card: null,
      badge: false,
    };
  }

  /**
   * Zooms the page, draws the overlay and starts capturing frames into `<outDir>/<video>/frames/`, emptying
   * any earlier run.
   *
   * @param page - The page to record, already showing the first scene's starting state. Its viewport should
   *   be 1920x1080 at device scale factor 1: the screencast captures CSS pixels, not device pixels.
   * @param options - The video's name, brand colour and output folder.
   * @returns The running demo.
   */
  static async start(page: Page, options: DemoOptions): Promise<Demo> {
    const full: Required<DemoOptions> = { size: { width: 1920, height: 1080 }, zoom: 4 / 3, ...options };
    const dir = join(full.outDir, full.video);
    rmSync(dir, { recursive: true, force: true });
    mkdirSync(join(dir, "frames"), { recursive: true });

    await page.evaluate(zoomPage, full.zoom);
    await page.waitForTimeout(300);
    const demo = new Demo(page, full, dir);
    await demo.render();
    await page.screencast.start({
      size: full.size,
      quality: 92,
      onFrame: ({ data }) => {
        // Stamp on arrival with the clock every other event uses; the delay is a few milliseconds.
        const file = `frames/${String(demo.frames.length + 1).padStart(6, "0")}.jpg`;
        writeFileSync(join(dir, file), data);
        demo.frames.push({ file, t: Date.now() });
      },
    });
    return demo;
  }

  /**
   * Shows the title card and marks the start of the video.
   *
   * @param card - Logo, name and promise.
   * @param ms - How long the card stays up once shown.
   */
  async titleCard(card: TitleCard, ms = 3_000): Promise<void> {
    this.setCaption(null);
    this.state.card =
      `<div class="inner"><img src="${escapeHtml(card.logo)}" alt=""><h1>${escapeHtml(card.title)}</h1>` +
      `<div class="bar"></div><p>${escapeHtml(card.promise)}</p></div>`;
    await this.render();
    // The video starts once the card has faded in, so it opens on the full card.
    await this.page.waitForTimeout(450);
    this.keepStart = Date.now();
    await this.hold(ms);
    this.state.card = null;
    await this.render();
    await this.page.waitForTimeout(400);
  }

  /**
   * Shows the end card, holds it and marks the end of the video.
   *
   * @param card - Install command and docs URL.
   * @param ms - How long the card stays up.
   */
  async endCard(card: EndCard, ms = 4_000): Promise<void> {
    this.clear();
    this.setCaption(null);
    this.state.card =
      `<div class="inner"><h1>Try it</h1><div class="bar"></div><code>${escapeHtml(card.install)}</code>` +
      `<p>${escapeHtml(card.docs)}</p></div>`;
    await this.render();
    await this.hold(ms + 400);
    this.keepEnd = Date.now();
  }

  /**
   * Shows a full-frame code card, for a step that happens outside the screen (configuration, a C#
   * registration, a command).
   *
   * @param code - The snippet, a few lines.
   * @param language - `csharp`, `json` or `shell`.
   * @param ms - How long the card stays up.
   */
  async codeCard(code: string, language: string, ms = 5_000): Promise<void> {
    this.clear();
    this.state.card = `<pre>${highlightCode(code, language)}</pre>`;
    await this.render();
    await this.hold(ms + 400);
    this.state.card = null;
    await this.render();
    await this.page.waitForTimeout(400);
  }

  /**
   * Starts a scene: clears the previous scene's labels and highlight and shows its caption.
   *
   * @param caption - The storyboard's caption for the scene.
   */
  async scene(caption: string): Promise<void> {
    this.clear();
    await this.caption(caption);
  }

  /**
   * Replaces the caption.
   *
   * @param text - The caption, or null to hide it.
   */
  async caption(text: string | null): Promise<void> {
    this.setCaption(text);
    await this.render();
  }

  /**
   * Glides the cursor to an element, then clicks it for real.
   *
   * @param target - The element to click.
   */
  async click(target: Locator): Promise<void> {
    await this.moveTo(target);
    this.state.cursor.pulse++;
    await this.render();
    await target.click();
  }

  /**
   * Clicks a text field and types into it at a readable speed.
   *
   * @param target - The input.
   * @param text - What to type.
   */
  async type(target: Locator, text: string): Promise<void> {
    await this.click(target);
    await target.pressSequentially(text, { delay: 55 });
  }

  /**
   * Presses a key in an element, e.g. Enter in a search box.
   *
   * @param target - The focused element.
   * @param key - A Playwright key name.
   */
  async press(target: Locator, key: string): Promise<void> {
    await this.page.waitForTimeout(250);
    await target.press(key);
  }

  /**
   * Rings an element and dims everything else. Replaces the previous highlight.
   *
   * @param target - The element.
   */
  async highlight(target: Locator): Promise<void> {
    await target.scrollIntoViewIfNeeded();
    this.spotTarget = target;
    await this.render();
  }

  /**
   * Pins a callout label to an element. Labels add up until the next scene or {@link Demo.clear}.
   *
   * @param target - The element the label points at.
   * @param text - The label, a few words.
   * @param prefer - `"right"` puts it beside the target (for wide targets); the default goes below or above.
   */
  async label(target: Locator, text: string, prefer: "auto" | "right" = "auto"): Promise<void> {
    await target.scrollIntoViewIfNeeded();
    this.labels.push({ target, text, prefer });
    await this.render();
  }

  /** Removes the labels and the highlight (the caption stays). */
  clear(): void {
    this.labels.length = 0;
    this.spotTarget = null;
  }

  /**
   * Keeps the current state on screen so the caption can be read.
   *
   * @param ms - How long.
   */
  async hold(ms: number): Promise<void> {
    await this.render();
    await this.page.waitForTimeout(ms);
  }

  /**
   * Waits for something slow. A wait over the fast-forward threshold (4 s) is squeezed to 2 s in the video;
   * a shorter one plays in real time.
   *
   * @param work - What to wait for.
   * @returns What the work resolved to.
   */
  async wait<T>(work: Promise<T> | (() => Promise<T>)): Promise<T> {
    const start = Date.now();
    const result = await (typeof work === "function" ? work() : work);
    const end = Date.now();
    if (isFastForward(end - start)) this.fastForwards.push({ start, end });
    return result;
  }

  /** Marks the current moment as the poster frame. */
  poster(): void {
    this.posterAt = Date.now();
  }

  /**
   * Stops recording and writes `timeline.json` and the fast-forward badge (`ff-badge.png`) for the renderer.
   *
   * @throws Error when the title or end card was never shown, so the video has no start or end.
   */
  async finish(): Promise<void> {
    await this.page.screencast.stop();
    if (this.keepStart === undefined || this.keepEnd === undefined) {
      throw new Error("A demo needs a titleCard() and an endCard() to mark where the video starts and ends.");
    }
    const open = this.captions.at(-1);
    if (open && open.end === Number.POSITIVE_INFINITY) open.end = this.keepEnd;

    // The badge is drawn by the overlay, so it matches the house style, and composited by the renderer.
    this.state.card = null;
    this.state.caption = null;
    this.state.badge = true;
    this.clear();
    await this.render();
    await this.page
      .locator("#demo-overlay .badge")
      .screenshot({ path: join(this.dir, "ff-badge.png"), omitBackground: true });

    const timeline: DemoTimeline = {
      video: this.options.video,
      size: this.options.size,
      keep: { start: this.keepStart, end: this.keepEnd },
      frames: this.frames,
      captions: this.captions,
      fastForwards: this.fastForwards,
      ...(this.posterAt === undefined ? {} : { poster: this.posterAt }),
    };
    writeFileSync(join(this.dir, "timeline.json"), JSON.stringify(timeline));
  }

  /** Closes the open caption and opens the next one, in the timeline. */
  private setCaption(text: string | null): void {
    const now = Date.now();
    const open = this.captions.at(-1);
    if (open && open.end === Number.POSITIVE_INFINITY) open.end = now;
    if (text !== null) this.captions.push({ start: now, end: Number.POSITIVE_INFINITY, text });
    this.state.caption = text;
  }

  /** The viewport in the overlay's CSS pixels (the page zoom divided out). */
  private viewport(): { width: number; height: number } {
    const size = this.page.viewportSize() ?? { width: 1920, height: 1080 };
    return { width: size.width / this.options.zoom, height: size.height / this.options.zoom };
  }

  /**
   * An element's box in the overlay's CSS pixels, or null when it has gone (a closed modal, say).
   * Playwright measures in viewport pixels; the overlay sits inside the zoomed page.
   */
  private async box(target: Locator): Promise<Box | null> {
    const box = await target.boundingBox({ timeout: 1_000 }).catch(() => null);
    if (!box) return null;
    const z = this.options.zoom;
    return { x: box.x / z, y: box.y / z, width: box.width / z, height: box.height / z };
  }

  /** Glides the cursor to an element's centre, taking longer for longer moves. */
  private async moveTo(target: Locator): Promise<void> {
    await target.scrollIntoViewIfNeeded();
    const box = await this.box(target);
    if (!box) throw new Error(`The demo target ${target.toString()} is not visible.`);
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    const distance = Math.hypot(x - this.state.cursor.x, y - this.state.cursor.y);
    const ms = Math.round(Math.min(900, Math.max(350, distance * 0.8)));
    this.state.cursor = { ...this.state.cursor, x, y, ms };
    await this.render();
    await this.page.waitForTimeout(ms + 80);
  }

  /** Measures the highlight and label targets again and sends the overlay state to the page. */
  private async render(): Promise<void> {
    const spot = this.spotTarget ? await this.box(this.spotTarget) : null;
    this.state.spot = spot && {
      x: spot.x - SPOT_PADDING,
      y: spot.y - SPOT_PADDING,
      width: spot.width + SPOT_PADDING * 2,
      height: spot.height + SPOT_PADDING * 2,
    };

    let labels = "";
    for (const label of this.labels) {
      const box = await this.box(label.target);
      if (!box) continue;
      const inner = escapeHtml(label.text);
      let size = this.labelSizes.get(inner);
      if (!size) {
        size = await this.page.evaluate(measureLabel, `<span class="arrow"></span>${inner}`);
        this.labelSizes.set(inner, size);
      }
      const p = calloutPlacement(box, size, this.viewport(), label.prefer);
      const arrow = p.side === "right" ? `top:${p.arrow}px` : `left:${p.arrow}px`;
      labels +=
        `<div class="label ${p.side}" style="left:${p.left}px;top:${p.top}px">` +
        `<span class="arrow" style="${arrow}"></span>${inner}</div>`;
    }
    this.state.labels = labels;

    await this.page.evaluate(renderOverlay, this.state);
  }
}
