// DOM helpers for working with bloom-canvas and bloom-canvas-element elements.
//
// Keep this module dependency-light so it can be used from either iframe bundle.

import {
    kCanvasElementClass,
    kHasCanvasElementClass,
} from "./canvasElementConstants";

// An xmatter page allows canvas elements only when the user has switched it to custom layout;
// in standard layout it has no way to hold one. Ordinary content pages always allow them.
// (A missing page counts as allowing them, since only xmatter pages restrict this.)
export const pageAllowsCanvasElements = (
    page: Element | null | undefined,
): boolean => {
    if (!page) {
        return true;
    }
    const isXmatterPage =
        page.classList.contains("bloom-frontMatter") ||
        page.classList.contains("bloom-backMatter");
    return !isXmatterPage || page.classList.contains("bloom-customLayout");
};

// For use by bloomImages.ts and other code that needs to keep the bloom canvas class
// in sync with whether it currently contains any canvas elements.
export const updateCanvasElementClass = (bloomCanvas: HTMLElement) => {
    if (bloomCanvas.getElementsByClassName(kCanvasElementClass).length > 0) {
        bloomCanvas.classList.add(kHasCanvasElementClass);
    } else {
        bloomCanvas.classList.remove(kHasCanvasElementClass);
    }
};
