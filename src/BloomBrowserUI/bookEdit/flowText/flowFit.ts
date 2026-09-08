// How much text fits in a box. This file must stay free of any text-measuring library, so
// that the modules that only need the interface do not pull one in; flowPretextMeasurer.ts
// is the implementation.

export type BoxMetrics = {
    /** A CSS shorthand font value, as the canvas 2d context takes it. */
    font: string;
    lineHeight: number;
    /** The content width in CSS pixels, padding already taken off. */
    width: number;
    /** The content height in CSS pixels, padding already taken off. */
    height: number;
};

export interface LineMeasurer {
    /**
     * The number of characters of text that fit in a box of these metrics, or the whole
     * length of text if all of it fits.
     */
    measureFit(text: string, metrics: BoxMetrics): number;
}

/**
 * The content box of an editable, in CSS pixels. clientWidth and clientHeight are before
 * any transform, so the page zoom does not enter into it.
 */
export function getBoxMetrics(editable: HTMLElement): BoxMetrics {
    const computed = window.getComputedStyle(editable);
    const paddingLeft = parseFloat(computed.paddingLeft || "0") || 0;
    const paddingRight = parseFloat(computed.paddingRight || "0") || 0;
    const paddingTop = parseFloat(computed.paddingTop || "0") || 0;
    const paddingBottom = parseFloat(computed.paddingBottom || "0") || 0;

    return {
        font: getCanvasFont(computed),
        lineHeight: getLineHeight(computed),
        width: Math.max(1, editable.clientWidth - paddingLeft - paddingRight),
        height: Math.max(1, editable.clientHeight - paddingTop - paddingBottom),
    };
}

/** The font shorthand, assembled from the longhands where the browser gives us no shorthand. */
export function getCanvasFont(computed: CSSStyleDeclaration): string {
    if (computed.font) {
        return computed.font;
    }

    return `${computed.fontStyle || "normal"} ${
        computed.fontVariant || "normal"
    } ${computed.fontWeight || "400"} ${
        computed.fontStretch || "normal"
    } ${computed.fontSize || "16px"} ${computed.fontFamily || "sans-serif"}`;
}

export function getLineHeight(computed: CSSStyleDeclaration): number {
    const parsed = parseFloat(computed.lineHeight || "");
    if (!Number.isNaN(parsed)) {
        return parsed;
    }

    // "normal", or nothing at all.
    const fontSize = parseFloat(computed.fontSize || "16");
    return Math.max(1, fontSize * 1.2);
}
