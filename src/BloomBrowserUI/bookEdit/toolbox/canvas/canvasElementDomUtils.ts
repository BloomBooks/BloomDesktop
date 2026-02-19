// DOM helpers for working with bloom-canvas and bloom-canvas-element elements.
//
// Keep this module dependency-light so it can be used from either iframe bundle.

import {
    kCanvasElementClass,
    kHasCanvasElementClass,
} from "./canvasElementConstants";

// For use by bloomImages.ts and other code that needs to keep the bloom canvas class
// in sync with whether it currently contains any canvas elements.
export const updateCanvasElementClass = (bloomCanvas: HTMLElement) => {
    if (bloomCanvas.getElementsByClassName(kCanvasElementClass).length > 0) {
        bloomCanvas.classList.add(kHasCanvasElementClass);
    } else {
        bloomCanvas.classList.remove(kHasCanvasElementClass);
    }
};
