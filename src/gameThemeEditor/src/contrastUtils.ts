// Minimal WCAG contrast helpers, inlined so the editor needs no extra dependency.
// See https://www.w3.org/TR/WCAG21/#dfn-contrast-ratio and #dfn-relative-luminance.

// Tools for resolving ANY CSS color the browser understands — named colors, 8-digit hex,
// rgb()/rgba(), and crucially color-mix() (which the targets use for transparency) — into exact
// [r,g,b,a] bytes. We let the DOM resolve the color (so color-mix() etc. are evaluated; a
// canvas alone can't), then read the painted pixel (so we don't depend on how getComputedStyle
// happens to serialize it). This is why a faint, mostly-transparent target was wrongly scoring
// high contrast: the color it was built from couldn't be parsed and fell back to opaque.
let probeEl: HTMLSpanElement | null = null;
let pixelCtx: CanvasRenderingContext2D | null = null;
const resolveCache = new Map<string, [number, number, number, number]>();

function getProbe(): HTMLSpanElement {
    if (!probeEl) {
        probeEl = document.createElement("span");
        probeEl.style.cssText =
            "position:absolute;left:-9999px;top:-9999px;width:0;height:0;";
        document.body.appendChild(probeEl);
    }
    return probeEl;
}

function getPixelCtx(): CanvasRenderingContext2D {
    if (!pixelCtx) {
        const canvas = document.createElement("canvas");
        canvas.width = 1;
        canvas.height = 1;
        pixelCtx = canvas.getContext("2d", { willReadFrequently: true })!;
    }
    return pixelCtx;
}

/**
 * Parse any CSS color string ("white", "#0058cc", "#rrggbbaa", "rgba(...)", "color-mix(...)") to
 * [r,g,b,a], with r/g/b in 0-255 and a (alpha) in 0-1. The DOM resolves the color and we read the
 * painted pixel, so functional colors and transparency come through exactly. Cached by input.
 */
export function parseColorToRgba(
    cssColor: string,
): [number, number, number, number] {
    const value = (cssColor || "").trim();
    const cached = resolveCache.get(value);
    if (cached) return cached;

    // 1. Let the DOM resolve the color (evaluates color-mix(), named colors, etc.). Resetting to
    //    transparent first means an unparseable value yields transparent rather than a stale color.
    const probe = getProbe();
    probe.style.color = "rgba(0, 0, 0, 0)";
    probe.style.color = value;
    const resolved = getComputedStyle(probe).color || "rgba(0, 0, 0, 0)";

    // 2. Paint the resolved color on a 1x1 canvas and read the exact bytes (canvas reliably parses
    //    whatever rgb()/rgba()/color() form getComputedStyle returns).
    const ctx = getPixelCtx();
    ctx.clearRect(0, 0, 1, 1);
    ctx.fillStyle = "rgba(0, 0, 0, 0)";
    ctx.fillStyle = resolved;
    ctx.fillRect(0, 0, 1, 1);
    const [r, g, b, a] = ctx.getImageData(0, 0, 1, 1).data;

    const result: [number, number, number, number] = [r, g, b, a / 255];
    resolveCache.set(value, result);
    return result;
}

/** Parse any CSS color string to [r,g,b] 0-255 (alpha dropped). */
export function parseColorToRgb(cssColor: string): [number, number, number] {
    const [r, g, b] = parseColorToRgba(cssColor);
    return [r, g, b];
}

/**
 * Flatten a (possibly translucent) top color over an opaque bottom color, returning the visible
 * "rgb(...)". Contrast must be measured against what the eye actually sees, so a nearly-transparent
 * color resolves close to its background (and so fails contrast) rather than to its nominal color.
 */
export function compositeOver(top: string, bottom: string): string {
    const [tr, tg, tb, ta] = parseColorToRgba(top);
    if (ta >= 1) return top;
    const [br, bg, bb] = parseColorToRgb(bottom);
    const mix = (t: number, b: number) => Math.round(t * ta + b * (1 - ta));
    return `rgb(${mix(tr, br)}, ${mix(tg, bg)}, ${mix(tb, bb)})`;
}

/** WCAG relative luminance of an sRGB color. */
function relativeLuminance([r, g, b]: [number, number, number]): number {
    const channel = (c: number) => {
        const s = c / 255;
        return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
    };
    return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/** WCAG contrast ratio (1..21) between two CSS colors. */
export function contrastRatio(colorA: string, colorB: string): number {
    const la = relativeLuminance(parseColorToRgb(colorA));
    const lb = relativeLuminance(parseColorToRgb(colorB));
    const lighter = Math.max(la, lb);
    const darker = Math.min(la, lb);
    return (lighter + 0.05) / (darker + 0.05);
}
