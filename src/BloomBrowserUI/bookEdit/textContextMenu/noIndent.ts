// The logic behind the "No Indent" command on the text context menu (BL-16649).
// Kept separate from the React component so it can be unit tested against a plain DOM.

import { kCanvasElementSelector } from "../toolbox/canvas/canvasElementUtils";

// Put on an individual <p> to cancel the first-line indent that the paragraph's style
// (see the Format dialog) would otherwise give it. The matching CSS rule is in
// basePage-sharedRules.less (and a copy in basePage-legacy-5-6.less).
export const kNoIndentClass = "bloom-noIndent";

/**
 * Find the paragraph that a right-click should act on, or undefined if this is not a click
 * the text context menu should handle. We only want paragraphs of ordinary text boxes:
 * text inside a canvas element has its own context menu, which the canvas code puts up.
 */
export function findParagraphForTextContextMenu(
    target: EventTarget | null,
): HTMLElement | undefined {
    if (!(target instanceof Element)) return undefined;
    const paragraph = target.closest("p");
    if (!paragraph) return undefined;
    if (!paragraph.closest(".bloom-editable")) return undefined;
    if (paragraph.closest(kCanvasElementSelector)) return undefined;
    return paragraph;
}

/** Is "No Indent" currently turned on for this paragraph? */
export function isNoIndentOn(paragraph: HTMLElement): boolean {
    return paragraph.classList.contains(kNoIndentClass);
}

/**
 * "No Indent" is worth offering only if it is already on (so the user can turn it back off),
 * or if something is actually indenting the paragraph, so that turning it on would do
 * something visible. A paragraph whose style has no first-line indent gets a disabled item.
 */
export function canToggleNoIndent(paragraph: HTMLElement): boolean {
    if (isNoIndentOn(paragraph)) return true;
    const indent =
        paragraph.ownerDocument.defaultView?.getComputedStyle(
            paragraph,
        ).textIndent;
    // A computed text-indent is an absolute length ("0px", "26.6667px") or a percentage.
    return !!indent && parseFloat(indent) !== 0;
}

/** Turn "No Indent" on or off for this one paragraph. */
export function toggleNoIndent(paragraph: HTMLElement): void {
    paragraph.classList.toggle(kNoIndentClass);
    // The class is plain content markup inside the bloom-editable, so it is saved with the
    // page the next time Bloom asks the browser for the page content; nothing to post here.
}
