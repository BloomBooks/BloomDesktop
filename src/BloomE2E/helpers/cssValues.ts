// Turn the values a browser reports for computed styles into the units a person sees in Bloom's UI.
//
// A primitive: surface modules use these when they read the page, so that a test compares what it
// asked for ("#ff1616", 17 points) with what the page shows, in the same terms, and never parses
// "rgb(255, 22, 22)" or "22.6667px" itself.

/**
 * A CSS color as the browser reports it ("rgb(255, 22, 22)", "rgba(0, 0, 0, 1)", "#FF1616",
 * "transparent") as lower-case "#rrggbb", or "transparent" when the color is fully transparent.
 * Throws for anything else, naming what it was given.
 */
export function cssColorToHex(cssColor: string): string {
    const value = cssColor.trim();
    if (value === "transparent") return "transparent";
    const rgb =
        /^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)$/.exec(
            value,
        );
    if (rgb) {
        if (rgb[4] !== undefined && Number(rgb[4]) === 0) return "transparent";
        return (
            "#" +
            [rgb[1], rgb[2], rgb[3]]
                .map((n) => Number(n).toString(16).padStart(2, "0"))
                .join("")
        );
    }
    const hex = /^#([0-9a-f]{6})([0-9a-f]{2})?$/i.exec(value);
    if (hex) {
        if (hex[2] !== undefined && parseInt(hex[2], 16) === 0)
            return "transparent";
        return "#" + hex[1].toLowerCase();
    }
    throw new Error(`"${cssColor}" is not a color this helper understands.`);
}

/** Pixels (as computed styles report them, e.g. "22.6667px") to points, rounded to 2 decimals. */
export function cssPxToPt(cssLength: string): number {
    const px = parseFloat(cssLength);
    if (Number.isNaN(px))
        throw new Error(`"${cssLength}" is not a length in pixels.`);
    return Math.round(px * 0.75 * 100) / 100;
}
