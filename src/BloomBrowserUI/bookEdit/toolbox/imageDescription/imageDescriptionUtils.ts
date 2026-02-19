import {
    kBloomCanvasClass,
    getCanvasElementManager,
} from "../canvas/canvasElementUtils";

// This file is intended to expose some image description functions that other parts of the
// code (in both iframes) need to use, while pulling in a minimum of dependencies.

export function showImageDescriptions(bodyOfPageIframe: HTMLElement) {
    const canvasElementManager = getCanvasElementManager();
    canvasElementManager?.suspendComicEditing("forTool");
    // turn on special layout to make image descriptions visible (might already be on, so check first)
    if (!bodyOfPageIframe.classList.contains("bloom-showImageDescriptions")) {
        bodyOfPageIframe.classList.add("bloom-showImageDescriptions");
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
}
export function hideImageDescriptions(bodyOfPageIframe: HTMLElement) {
    const canvasElementManager = getCanvasElementManager();
    // removing the class and wrapper should be done first; resume may not work
    // right while the extra wrapper is present.
    bodyOfPageIframe.classList.remove("bloom-showImageDescriptions");
    // unwrap the contents of each bloom-describedImage
    for (const describedImage of Array.from(
        bodyOfPageIframe.getElementsByClassName("bloom-describedImage"),
    )) {
        for (const child of Array.from(describedImage.children)) {
            describedImage.parentElement!.appendChild(child);
        }
        describedImage.remove();
    }
    canvasElementManager?.resumeComicEditing();
}
