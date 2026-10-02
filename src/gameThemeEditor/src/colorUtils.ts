// Small color helpers for the editor's color picker and swatches: converting arbitrary CSS
// color strings ("white", "rgb(0,88,204)", "#0058cc", "#rrggbbaa", …) to/from the HSVA model
// the picker edits, deduping swatches, and drawing transparency.

import { parseColorToRgb, parseColorToRgba } from "./contrastUtils";

// CSS for a light/grey checkerboard, drawn behind a (possibly translucent) color so its
// transparency is visible. Put the real color in a layer on top of an element with this.
export const checkerboardBackground = `
    background-color: white;
    background-image:
        linear-gradient(45deg, #c8c8c8 25%, transparent 25%),
        linear-gradient(-45deg, #c8c8c8 25%, transparent 25%),
        linear-gradient(45deg, transparent 75%, #c8c8c8 75%),
        linear-gradient(-45deg, transparent 75%, #c8c8c8 75%);
    background-size: 8px 8px;
    background-position: 0 0, 0 4px, 4px -4px, -4px 0;
`;

/** Canonical "#rrggbb" (opaque) or "#rrggbbaa" (translucent) form of any CSS color string. */
export const toCanonicalColor = (cssColor: string): string => {
    const [r, g, b, a] = parseColorToRgba(cssColor);
    const h2 = (n: number) => n.toString(16).padStart(2, "0");
    const base = `#${h2(r)}${h2(g)}${h2(b)}`;
    return a >= 1 ? base : base + h2(Math.round(a * 255));
};

// Two colors count as "the same to the eye" when every channel is within this many 0-255 steps
// and their alpha within a small fraction. Used only to avoid listing near-identical swatches
// twice; the values stored when a swatch is clicked are still the exact theme colors.
const kSwatchMergeTolerance = 12;
const kAlphaMergeTolerance = 0.06;

const colorsLookAlike = (a: string, b: string): boolean => {
    const [ar, ag, ab, aa] = parseColorToRgba(a);
    const [br, bg, bb, ba] = parseColorToRgba(b);
    return (
        Math.abs(ar - br) <= kSwatchMergeTolerance &&
        Math.abs(ag - bg) <= kSwatchMergeTolerance &&
        Math.abs(ab - bb) <= kSwatchMergeTolerance &&
        Math.abs(aa - ba) <= kAlphaMergeTolerance
    );
};

// HSV is the natural model for the picker's spectrum (a saturation/value square plus a hue
// slider), so we convert to/from it here. h is in [0,360); s and v are in [0,1].
export interface Hsv {
    h: number;
    s: number;
    v: number;
}

const rgbToHsv = (r: number, g: number, b: number): Hsv => {
    const rr = r / 255;
    const gg = g / 255;
    const bb = b / 255;
    const max = Math.max(rr, gg, bb);
    const min = Math.min(rr, gg, bb);
    const d = max - min;
    let h = 0;
    if (d !== 0) {
        if (max === rr) h = ((gg - bb) / d) % 6;
        else if (max === gg) h = (bb - rr) / d + 2;
        else h = (rr - gg) / d + 4;
        h *= 60;
        if (h < 0) h += 360;
    }
    return { h, s: max === 0 ? 0 : d / max, v: max };
};

/** The HSV of any CSS color string. */
export const colorToHsv = (cssColor: string): Hsv => {
    const [r, g, b] = parseColorToRgb(cssColor);
    return rgbToHsv(r, g, b);
};

/** "#rrggbb" for an HSV color. */
export const hsvToHex = (hsv: Hsv): string => {
    const c = hsv.v * hsv.s;
    const x = c * (1 - Math.abs(((hsv.h / 60) % 2) - 1));
    const m = hsv.v - c;
    let rr = 0;
    let gg = 0;
    let bb = 0;
    if (hsv.h < 60) [rr, gg] = [c, x];
    else if (hsv.h < 120) [rr, gg] = [x, c];
    else if (hsv.h < 180) [gg, bb] = [c, x];
    else if (hsv.h < 240) [gg, bb] = [x, c];
    else if (hsv.h < 300) [rr, bb] = [x, c];
    else [rr, bb] = [c, x];
    const toHex = (n: number) =>
        Math.round((n + m) * 255)
            .toString(16)
            .padStart(2, "0");
    return `#${toHex(rr)}${toHex(gg)}${toHex(bb)}`;
};

// HSV plus an alpha channel (a in [0,1]), for a picker that can edit transparency.
export interface Hsva extends Hsv {
    a: number;
}

/** The HSVA of any CSS color string. */
export const colorToHsva = (cssColor: string): Hsva => {
    const [r, g, b, a] = parseColorToRgba(cssColor);
    return { ...rgbToHsv(r, g, b), a };
};

/** "#rrggbb" (opaque) or "#rrggbbaa" (translucent) for an HSVA color. */
export const hsvaToColor = (hsva: Hsva): string => {
    const base = hsvToHex(hsva);
    if (hsva.a >= 1) return base;
    const aa = Math.round(Math.min(1, Math.max(0, hsva.a)) * 255)
        .toString(16)
        .padStart(2, "0");
    return base + aa;
};

/**
 * Build the swatch palette for the picker: black, white, then each distinct color currently
 * used in the theme (passed in as CSS color strings). Near-identical colors are merged so the
 * user doesn't see what looks like the same color twice; the first occurrence wins, so the
 * representative is an exact theme color. Returns canonical "#rrggbb"/"#rrggbbaa" strings
 * (alpha preserved, so a translucent theme color stays translucent in the palette).
 */
export const buildThemeSwatches = (themeColors: string[]): string[] => {
    const result: string[] = [];
    const add = (cssColor: string) => {
        const canonical = toCanonicalColor(cssColor);
        if (!result.some((existing) => colorsLookAlike(existing, canonical)))
            result.push(canonical);
    };
    add("#000000");
    add("#ffffff");
    themeColors.forEach(add);
    return result;
};
