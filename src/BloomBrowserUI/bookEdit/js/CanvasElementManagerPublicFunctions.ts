// Small public helpers that other modules can call without importing the full
// CanvasElementManager class.
//
// This file exists to keep CanvasElementManager.ts smaller and to reduce coupling
// between the page bundle and toolbox UI code.

import { kCanvasToolId } from "../toolbox/toolIds";
import { getToolboxBundleExports } from "./bloomFrames";
import { kImageContainerClass } from "./bloomImages";

// This is just for debugging. It produces a string that describes the canvas element, generally
// well enough to identify it in console.log.
export const canvasElementDescription = (
    e: Element | null | undefined,
): string => {
    const elt = e as HTMLElement;
    if (!elt) {
        return "no canvas element";
    }
    const result =
        "canvas element at (" + elt.style.left + ", " + elt.style.top + ") ";
    const imageContainer = elt.getElementsByClassName(kImageContainerClass)[0];
    if (imageContainer) {
        const img = (imageContainer as HTMLElement).getElementsByTagName(
            "img",
        )[0];
        if (img) {
            return result + "with image : " + img.getAttribute("src");
        }
    }
    const videoSrc = elt.getElementsByTagName("source")[0];
    if (videoSrc) {
        return result + "with video " + videoSrc.getAttribute("src");
    }
    // Enhance: look for videoContainer similarly
    return result + "with text " + elt.innerText;
};

export const showCanvasTool = () => {
    getToolboxBundleExports()
        ?.getTheOneToolbox()
        .activateToolFromId(kCanvasToolId);
};
