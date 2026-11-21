import { getCanvasElementManager } from "../canvas/canvasElementPageBridge";
import { kBloomCanvasClass } from "../canvas/canvasElementConstants";
import { getToolboxBundleExports } from "../../js/workspaceFrames";

// This file is intended to expose some image description functions that other parts of the
// code (in both iframes) need to use, while pulling in a minimum of dependencies.

// The class on the page iframe's body that shows the image descriptions.
export const kShowImageDescriptionsClass = "bloom-showImageDescriptions";

// True while the page in this body is showing its image descriptions (and so a game page on it
// cannot be in Play).
export function areImageDescriptionsShowing(
    bodyOfPageIframe: HTMLElement,
): boolean {
    return bodyOfPageIframe.classList.contains(kShowImageDescriptionsClass);
}

export function showImageDescriptions(bodyOfPageIframe: HTMLElement) {
    // Image descriptions never show while a game page is in Play (see isGamePageInPlay).
    if (getToolboxBundleExports()?.isGamePageInPlay()) {
        return;
    }
    const canvasElementManager = getCanvasElementManager();
    canvasElementManager?.suspendCanvasElementEditing("forTool");
    // turn on special layout to make image descriptions visible (might already be on, so check first)
    if (!areImageDescriptionsShowing(bodyOfPageIframe)) {
        bodyOfPageIframe.classList.add(kShowImageDescriptionsClass);
        // for each bloom-canvas,
        // wrap the contents (except the description) with another division with class bloom-describedImage
        // See comment in editMode.less under bloom-describedImage for why we do this.
        for (const bloomCanvas of Array.from(
            bodyOfPageIframe.getElementsByClassName(kBloomCanvasClass),
        )) {
            const describedImage = document.createElement("div");
            describedImage.classList.add("bloom-describedImage");
            for (const child of Array.from(bloomCanvas.children)) {
                if (!child.classList.contains("bloom-imageDescription")) {
                    describedImage.appendChild(child);
                }
            }
            bloomCanvas.appendChild(describedImage);
        }
    }
    getToolboxBundleExports()?.redrawGameTabs(); // Play is no longer available
}
export function hideImageDescriptions(bodyOfPageIframe: HTMLElement) {
    const canvasElementManager = getCanvasElementManager();
    // removing the class and wrapper should be done first; resume may not work
    // right while the extra wrapper is present.
    bodyOfPageIframe.classList.remove(kShowImageDescriptionsClass);
    unwrapDescribedImages(bodyOfPageIframe);
    canvasElementManager?.resumeCanvasElementEditing();
    getToolboxBundleExports()?.redrawGameTabs(); // Play is available again
}

// Undo the bloom-describedImage wrapper that showImageDescriptions() adds around the non-description
// contents of each bloom-canvas. It touches only the DOM under 'root', so the tools' removeToolMarkup
// can run it on a clone of the page being saved.
export function unwrapDescribedImages(root: HTMLElement) {
    for (const describedImage of Array.from(
        root.getElementsByClassName("bloom-describedImage"),
    )) {
        for (const child of Array.from(describedImage.children)) {
            describedImage.parentElement!.appendChild(child);
        }
        describedImage.remove();
    }
}
